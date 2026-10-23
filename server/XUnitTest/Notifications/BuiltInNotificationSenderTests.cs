using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using DataAccess.InternalService;
using DataAccess.InternalService.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// The one sender of the built-in notifications: correct request, never throws, never waits past its
    /// timeout, repeats of an alert are limited, and the payload carries no secret.
    /// </summary>
    public class BuiltInNotificationSenderTests
    {
        private const string Url = "https://notify.example.com/Notifier/SendSecretNotification";
        private const string Tenant = "tenant-1";
        private const string SecretValue = "hashed-secret-value";

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

            public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null)
            {
                _respond = respond ?? ((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            }

            public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
                return await _respond(request, cancellationToken);
            }
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }

        private static (BuiltInNotificationSender Sender, StubHandler Handler, FakeTime Time, CapturedLogger Log) Build(
            StubHandler? handler = null,
            string? url = Url,
            bool knownTenant = true,
            Dictionary<string, string?>? extra = null)
        {
            handler ??= new StubHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

            var settings = new Dictionary<string, string?>();
            if (url is not null) settings["NotificationServiceUrl"] = url;
            foreach (var (k, v) in extra ?? []) settings[k] = v;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var crypto = new Mock<ICryptoService>();
            crypto.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>())).Returns(SecretValue);

            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(Tenant)).Returns(knownTenant
                ? new Tenant
                {
                    TenantId = Tenant,
                    TenantSalt = "salt",
                    DbConnectionString = "conn",
                    JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "x", IssueDate = DateTime.UtcNow },
                }
                : null);

            var time = new FakeTime();
            var log = new CapturedLogger();
            return (new BuiltInNotificationSender(configuration, tenants.Object, crypto.Object, factory.Object, log, time), handler, time, log);
        }

        private static BuiltInNotificationPayload Payload(string description = "A short sentence.") =>
            new("TestEvent", "Title", description, IsSuccess: false,
                Ids: new Dictionary<string, string?> { ["functionId"] = "fn-1" });

        private sealed class CapturedLogger : ILogger<BuiltInNotificationSender>
        {
            public List<(LogLevel Level, string Text)> Lines { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Lines.Add((logLevel, formatter(state, exception)));
        }

        // ---- H12: the request ------------------------------------------------------------

        [Fact]
        public async Task A_send_posts_the_config_name_with_the_tenant_headers()
        {
            var (sender, handler, _, _) = Build();

            var sent = await sender.SendAsync(Tenant, "logic-function-run-failed", ["user-1"], Payload());

            sent.Should().BeTrue();
            var (request, body) = handler.Calls.Should().ContainSingle().Subject;
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be(Url);
            request.Headers.GetValues("x-blocks-key").Should().Equal(Tenant);
            request.Headers.GetValues("Secret").Should().Equal(SecretValue);

            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("configurationName").GetString().Should().Be("logic-function-run-failed");
            json.RootElement.GetProperty("userIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("user-1");
            json.RootElement.GetProperty("roles").GetArrayLength().Should().Be(0);
        }

        [Fact]
        public async Task The_payload_has_the_event_ids_and_time_only()
        {
            var (sender, handler, _, _) = Build();

            await sender.SendAsync(Tenant, "cfg", ["user-1"], Payload());

            using var outer = JsonDocument.Parse(handler.Calls.Single().Body);
            using var payload = JsonDocument.Parse(outer.RootElement.GetProperty("denormalizedPayload").GetString()!);
            var names = payload.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            names.Should().BeEquivalentTo("event", "title", "description", "isSuccess", "functionId", "occurredAt");
            payload.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        }

        [Fact]
        public async Task The_secret_header_value_is_never_in_the_body_or_the_logs()
        {
            var (sender, handler, _, log) = Build();

            await sender.SendAsync(Tenant, "cfg", ["user-1"], Payload());

            handler.Calls.Single().Body.Should().NotContain(SecretValue);
            log.Lines.Should().NotContain(l => l.Text.Contains(SecretValue, StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_long_description_is_cut()
        {
            var (sender, handler, _, _) = Build();

            await sender.SendAsync(Tenant, "cfg", ["user-1"], Payload(new string('x', 500)));

            using var outer = JsonDocument.Parse(handler.Calls.Single().Body);
            using var payload = JsonDocument.Parse(outer.RootElement.GetProperty("denormalizedPayload").GetString()!);
            payload.RootElement.GetProperty("description").GetString()!.Length.Should().Be(BuiltInNotificationSender.MaxDescriptionLength);
        }

        [Fact]
        public async Task Tenant_admins_are_addressed_by_role_with_the_default_and_the_configured_names()
        {
            var (sender, handler, _, _) = Build();
            await sender.SendToTenantAdminsAsync(Tenant, "cfg", Payload());
            using (var json = JsonDocument.Parse(handler.Calls.Single().Body))
            {
                json.RootElement.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).Should().Equal("admin");
                json.RootElement.GetProperty("userIds").GetArrayLength().Should().Be(0);
            }

            var (custom, customHandler, _, _) = Build(extra: new() { ["BuiltInNotification:AdminRoles"] = "admin, owner" });
            await custom.SendToTenantAdminsAsync(Tenant, "cfg", Payload());
            using var json2 = JsonDocument.Parse(customHandler.Calls.Single().Body);
            json2.RootElement.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).Should().Equal("admin", "owner");
        }

        // ---- C1, C2: never breaks, never waits ---------------------------------------------

        [Fact]
        public async Task A_failing_notifier_returns_false_and_logs_one_warning_without_secrets()
        {
            var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
            var (sender, _, _, log) = Build(handler);

            var sent = await sender.SendAsync(Tenant, "logic-mail-send-failed", ["user-1"], Payload());

            sent.Should().BeFalse();
            var warning = log.Lines.Should().ContainSingle(l => l.Level == LogLevel.Warning).Subject.Text;
            warning.Should().Contain("logic-mail-send-failed").And.Contain(Tenant).And.Contain("HTTP 500");
            warning.Should().NotContain(SecretValue).And.NotContain(Url);
        }

        [Fact]
        public async Task A_throwing_http_call_is_swallowed_and_only_the_exception_type_is_logged()
        {
            var handler = new StubHandler((_, _) => throw new HttpRequestException("connection to https://secret-host failed"));
            var (sender, _, _, log) = Build(handler);

            var sent = await sender.SendAsync(Tenant, "cfg", ["user-1"], Payload());

            sent.Should().BeFalse();
            log.Lines.Should().NotContain(l => l.Text.Contains("secret-host", StringComparison.Ordinal));
            log.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Text.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_hanging_notifier_is_abandoned_at_the_timeout()
        {
            var handler = new StubHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var (sender, _, _, log) = Build(handler);

            var started = DateTime.UtcNow;
            var sent = await sender.SendAsync(Tenant, "cfg", ["user-1"], Payload());

            sent.Should().BeFalse();
            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
            BuiltInNotificationSender.SendTimeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
            log.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Text.Contains("timeout", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_caller_cancel_is_not_reported_as_a_timeout_and_does_not_throw()
        {
            var (sender, handler, _, _) = Build(new StubHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var act = async () => await sender.SendAsync(Tenant, "cfg", ["user-1"], Payload(), cancellationToken: cts.Token);

            // The caller's own cancel surfaces as an exception only if the token is the caller's; the sender
            // contract is "never throws", so it must come back false.
            var result = await act.Should().NotThrowAsync();
            result.Subject.Should().BeFalse();
            handler.Calls.Should().HaveCount(1);
        }

        [Fact]
        public async Task A_missing_url_an_unknown_tenant_or_no_tenant_send_nothing()
        {
            var (noUrl, noUrlHandler, _, _) = Build(url: null);
            (await noUrl.SendAsync(Tenant, "cfg", ["user-1"], Payload())).Should().BeFalse();
            noUrlHandler.Calls.Should().BeEmpty();

            var (unknown, unknownHandler, _, _) = Build(knownTenant: false);
            (await unknown.SendAsync(Tenant, "cfg", ["user-1"], Payload())).Should().BeFalse();
            unknownHandler.Calls.Should().BeEmpty();

            var (blank, blankHandler, _, _) = Build();
            (await blank.SendAsync(" ", "cfg", ["user-1"], Payload())).Should().BeFalse();
            blankHandler.Calls.Should().BeEmpty();
        }

        [Fact]
        public async Task No_recipient_sends_nothing()
        {
            var (sender, handler, _, _) = Build();

            (await sender.SendAsync(Tenant, "cfg", null, Payload())).Should().BeFalse();
            (await sender.SendAsync(Tenant, "cfg", [], Payload())).Should().BeFalse();
            (await sender.SendAsync(Tenant, "cfg", [" ", ""], Payload())).Should().BeFalse();

            handler.Calls.Should().BeEmpty();
        }

        // ---- C3, C13: throttle ----------------------------------------------------------------

        [Fact]
        public async Task A_repeated_alert_inside_the_window_is_sent_once_and_again_after_it()
        {
            var (sender, handler, time, _) = Build();

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-1");
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-1");
            handler.Calls.Should().HaveCount(1);

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-2");
            handler.Calls.Should().HaveCount(2, "another key is another alert");

            time.Now = time.Now.AddSeconds(BuiltInNotificationSender.DefaultThrottleSeconds + 1);
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-1");
            handler.Calls.Should().HaveCount(3);
        }

        [Fact]
        public async Task Without_a_throttle_key_every_call_is_sent()
        {
            var (sender, handler, _, _) = Build();

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload());
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload());

            handler.Calls.Should().HaveCount(2);
        }

        [Fact]
        public async Task The_throttle_window_can_be_set()
        {
            var (sender, handler, time, _) = Build(extra: new() { ["BuiltInNotification:ThrottleSeconds"] = "10" });

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "k");
            time.Now = time.Now.AddSeconds(11);
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "k");

            handler.Calls.Should().HaveCount(2);
        }

        [Fact]
        public async Task The_throttle_map_does_not_grow_without_bound()
        {
            var (sender, handler, _, _) = Build();

            for (var i = 0; i < BuiltInNotificationSender.MaxThrottleKeys * 3; i++)
            {
                await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: $"k-{i}");
            }

            handler.Calls.Should().HaveCount(BuiltInNotificationSender.MaxThrottleKeys * 3);
            var field = typeof(BuiltInNotificationSender).GetField("_lastSent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            ((System.Collections.ICollection)field.GetValue(sender)!).Count.Should().BeLessThanOrEqualTo(BuiltInNotificationSender.MaxThrottleKeys);
        }

        // ---- registration -------------------------------------------------------------------

        [Fact]
        public void The_sender_is_registered_by_AddLogicDataAccess_so_every_host_gets_it()
        {
            var services = new ServiceCollection();
            services.AddLogicDataAccess();

            services.Should().Contain(d =>
                d.ServiceType == typeof(IBuiltInNotificationSender)
                && d.ImplementationType == typeof(BuiltInNotificationSender)
                && d.Lifetime == ServiceLifetime.Singleton);
        }

        [Fact]
        public void Every_config_name_matches_the_seed_names_of_this_service()
        {
            new[]
            {
                BuiltInNotificationNames.WorkflowRunFailed, BuiltInNotificationNames.ScheduledTriggerDropped,
                BuiltInNotificationNames.FunctionBuildFinished, BuiltInNotificationNames.FunctionRunFailed,
                BuiltInNotificationNames.MailSendFailed, BuiltInNotificationNames.MailboxSyncFailing,
                BuiltInNotificationNames.ScheduleRegistrationFailed, BuiltInNotificationNames.FunctionChangedByOther,
            }.Should().BeEquivalentTo(
                "logic-workflow-run-failed", "logic-scheduled-trigger-dropped", "logic-function-build-finished",
                "logic-function-run-failed", "logic-mail-send-failed", "logic-mailbox-sync-failing",
                "logic-schedule-registration-failed", "logic-function-changed-by-other");
        }
    }
}
