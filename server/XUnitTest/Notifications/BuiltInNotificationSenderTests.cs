using System.Text.Json;
using Blocks.Genesis.Notifications;
using DataAccess.InternalService;
using DataAccess.InternalService.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// The one sender of logic's built-in notifications: it hands them to the genesis publisher (the tenant's
    /// configuration decides receivers), limits repeats of an alert, and the payload carries ids and fixed text only.
    /// </summary>
    public class BuiltInNotificationSenderTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private sealed class FakePublisher : INotificationPublisher
        {
            public List<NotificationRequest> Requests { get; } = [];

            public void Publish(NotificationRequest request) => Requests.Add(request);
        }

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static (BuiltInNotificationSender Sender, FakePublisher Publisher, FakeTime Time) Build(Dictionary<string, string?>? settings = null)
        {
            var publisher = new FakePublisher();
            var time = new FakeTime();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
            return (new BuiltInNotificationSender(configuration, publisher, NullLogger<BuiltInNotificationSender>.Instance, time), publisher, time);
        }

        private static BuiltInNotificationPayload Payload(string description = "A short sentence.") =>
            new("TestEvent", "Title", description, IsSuccess: false,
                Ids: new Dictionary<string, string?> { ["functionId"] = "fn-1" });

        [Fact]
        public async Task A_send_publishes_the_config_the_tenant_and_the_named_users()
        {
            var (sender, publisher, _) = Build();

            var queued = await sender.SendAsync(Tenant, "logic-function-run-failed", ["u1", "u1", " "], Payload());

            queued.Should().BeTrue();
            var request = publisher.Requests.Single();
            request.Name.Should().Be("logic-function-run-failed");
            request.TenantId.Should().Be(Tenant);
            request.NamedUserIds.Should().Equal("u1");
            request.ResponseKey.Should().Be("logic-function-run-failed");
            request.ResponseValue.Should().Be("False");
        }

        [Fact]
        public async Task The_payload_has_the_event_ids_and_time_only_and_a_long_description_is_cut()
        {
            var (sender, publisher, _) = Build();

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(new string('x', 500)));

            using var json = JsonDocument.Parse((string)publisher.Requests.Single().Payload!);
            json.RootElement.EnumerateObject().Select(p => p.Name).Should()
                .BeEquivalentTo("event", "title", "description", "isSuccess", "functionId", "occurredAt");
            json.RootElement.GetProperty("description").GetString().Should().HaveLength(BuiltInNotificationSender.MaxDescriptionLength);
        }

        [Fact]
        public async Task Tenant_admin_sends_name_nobody_so_the_configuration_audience_decides()
        {
            var (sender, publisher, _) = Build();

            await sender.SendToTenantAdminsAsync(Tenant, "logic-mail-send-failed", Payload());

            publisher.Requests.Single().NamedUserIds.Should().BeEmpty();
        }

        [Fact]
        public async Task No_recipient_or_no_tenant_publishes_nothing()
        {
            var (sender, publisher, _) = Build();

            (await sender.SendAsync(Tenant, "cfg", [], Payload())).Should().BeFalse();
            (await sender.SendAsync(Tenant, "cfg", null, Payload())).Should().BeFalse();
            (await sender.SendAsync("", "cfg", ["u"], Payload())).Should().BeFalse();

            publisher.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task The_context_organization_is_sent_only_for_the_same_tenant()
        {
            var (sender, publisher, _) = Build();

            TestBlocksContext.Set(Tenant, "actor", "org-a");
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload());
            TestBlocksContext.Set("another-tenant", "actor", "org-b");
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload());

            publisher.Requests.Select(r => r.OrganizationId).Should().Equal("org-a", null);
        }

        [Fact]
        public async Task A_repeated_alert_inside_the_window_is_sent_once_and_again_after_it()
        {
            var (sender, publisher, time) = Build();

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-1");
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-1");
            publisher.Requests.Should().HaveCount(1);

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-2");
            publisher.Requests.Should().HaveCount(2, "another key is another alert");

            time.Now = time.Now.AddSeconds(BuiltInNotificationSender.DefaultThrottleSeconds + 1);
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "wf-1");
            publisher.Requests.Should().HaveCount(3);
        }

        [Fact]
        public async Task The_throttle_window_can_be_set()
        {
            var (sender, publisher, time) = Build(new() { ["BuiltInNotification:ThrottleSeconds"] = "10" });

            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "k");
            time.Now = time.Now.AddSeconds(11);
            await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: "k");

            publisher.Requests.Should().HaveCount(2);
        }

        [Fact]
        public async Task The_throttle_map_does_not_grow_without_bound()
        {
            var (sender, publisher, _) = Build();

            for (var i = 0; i < BuiltInNotificationSender.MaxThrottleKeys * 3; i++)
            {
                await sender.SendAsync(Tenant, "cfg", ["u"], Payload(), throttleKey: $"k-{i}");
            }

            publisher.Requests.Should().HaveCount(BuiltInNotificationSender.MaxThrottleKeys * 3);
            var field = typeof(BuiltInNotificationSender).GetField("_lastSent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            ((System.Collections.ICollection)field.GetValue(sender)!).Count.Should().BeLessThanOrEqualTo(BuiltInNotificationSender.MaxThrottleKeys);
        }

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
                BuiltInNotificationNames.MailSendFailed,
                BuiltInNotificationNames.ScheduleRegistrationFailed, BuiltInNotificationNames.FunctionChangedByOther,
            }.Should().BeEquivalentTo(
                "logic-workflow-run-failed", "logic-scheduled-trigger-dropped", "logic-function-build-finished",
                "logic-function-run-failed", "logic-mail-send-failed",
                "logic-schedule-registration-failed", "logic-function-changed-by-other");
        }
    }
}
