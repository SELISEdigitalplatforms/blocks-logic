using System.Net;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Enums;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// Unit tests for the webhook branch of <see cref="SchedulePublisherService"/>:
    /// HMAC signature computation, header application (incl. Content-Type skip),
    /// method/URL usage, tenant-context bypass, and the fire-and-forget contract
    /// (non-2xx and network failures are logged, never thrown, so Hangfire does not retry).
    /// </summary>
    public class SchedulePublisherServiceTests : IDisposable
    {
        private readonly Mock<IScheduleRepository> _scheduleRepository = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly List<CapturedRequest> _sent = [];
        private SchedulePublisherService _service;
        public SchedulePublisherServiceTests()
        {
            _service = CreateService(HttpStatusCode.OK);
        }

        public void Dispose() => _service.Dispose();

        private SchedulePublisherService CreateService(HttpStatusCode code, bool fail = false)
        {
            _service?.Dispose();
            var handler = new StubHandler(code, _sent, fail);
            var service = new TestableSchedulePublisherService(
                handler,
                Mock.Of<ILogger<SchedulePublisherService>>(),
                _scheduleRepository.Object,
                _messageClient.Object,
                _tenants.Object);
            _service = service;
            return service;
        }

        private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? Body, string? ContentType, List<(string Name, string Value)> Headers);

        private sealed class StubHandler(HttpStatusCode code, List<CapturedRequest> seen, bool fail) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Snapshot before the caller disposes the request — assertions run post-send.
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                var contentType = request.Content?.Headers.ContentType?.MediaType;
                var headers = request.Headers.SelectMany(h => h.Value.Select(v => (h.Key, v))).ToList();
                seen.Add(new CapturedRequest(request.Method, request.RequestUri, body, contentType, headers));
                if (fail) throw new HttpRequestException("network down");
                return Task.FromResult(new HttpResponseMessage(code)).Result;
            }
        }

        private sealed class TestableSchedulePublisherService(
            HttpMessageHandler handler,
            ILogger<SchedulePublisherService> logger,
            IScheduleRepository scheduleRepository,
            IMessageClient messageClient,
            ITenants tenants) : SchedulePublisherService(logger, scheduleRepository, messageClient, tenants)
        {
            // Exposes the internal HTTP client construction for testing by replacing the
            // client's handler through the protected hook below.
            public HttpClient TestClient { get; } = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            protected override HttpClient CreateHttpClient() => TestClient;
        }

        private Schedule WebhookSchedule(
            string payload = "{\"a\":1}",
            Dictionary<string, string>? headers = null,
            string? signingSecret = null,
            string method = "POST",
            string url = "https://webhook.example.com/hook") => new()
            {
                ItemId = "sched-1",
                IsActive = true,
                Kind = ScheduleKind.Application,
                TriggerType = ScheduleTriggerType.Webhook,
                Payload = payload,
                CronExpression = "0 9 * * *",
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(1),
                Webhook = new WebhookConfiguration
                {
                    Url = url,
                    Method = method,
                    Headers = headers,
                    SigningSecret = signingSecret,
                },
            };

        private void SetupSchedule(Schedule schedule)
            => _scheduleRepository.Setup(r => r.GetByIdAsync(schedule.ItemId, It.IsAny<string>())).ReturnsAsync(schedule);

        private static async Task<string> ReadContent(CapturedRequest request)
            => request.Body ?? string.Empty;

        // ---------- Signature ----------

        [Fact]
        public void ComputeSignature_MatchesKnownHmacSha256Hex()
        {
            // HMACSHA256(key="secret", payload="{\"a\":1}") — recomputed reference vector.
            var signature = SchedulePublisherService.ComputeSignature("secret", "{\"a\":1}");

            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("secret"));
            var expected = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes("{\"a\":1}"))).ToLowerInvariant();

            signature.Should().Be(expected);
            signature.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        // ---------- Webhook delivery ----------

        [Fact]
        public async Task Publish_WebhookSchedule_SendsConfiguredRequest()
        {
            var schedule = WebhookSchedule(headers: new Dictionary<string, string> { ["X-Api-Key"] = "key-1" }, signingSecret: "s3cret");
            SetupSchedule(schedule);

            CreateService(HttpStatusCode.OK);
            await _service.Publish(schedule.ItemId, "tenant-1");

            var request = _sent.Should().HaveCount(1).And.Subject.Single();
            request.Method.Method.Should().Be("POST");
            request.RequestUri!.ToString().Should().Be("https://webhook.example.com/hook");
            request.Body.Should().Be("{\"a\":1}");
            request.ContentType.Should().Be("application/json");
            request.Headers.Should().Contain(h => h.Name == "X-Api-Key" && h.Value == "key-1");
            request.Headers.Should().Contain(h => h.Name == "x-signature-sha256" &&
                h.Value == SchedulePublisherService.ComputeSignature("s3cret", "{\"a\":1}"));
        }

        [Fact]
        public async Task Publish_WebhookSchedule_SkipsIncomingContentTypeHeader()
        {
            var schedule = WebhookSchedule(headers: new Dictionary<string, string>
            {
                ["Content-Type"] = "text/plain",
                ["X-Custom"] = "yes",
            });
            SetupSchedule(schedule);

            CreateService(HttpStatusCode.OK);
            await _service.Publish(schedule.ItemId, "tenant-1");

            var request = _sent.Single();
            request.ContentType.Should().Be("application/json");
            request.Headers.Should().NotContain(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            request.Headers.Should().Contain(h => h.Name == "X-Custom" && h.Value == "yes");
        }

        [Fact]
        public async Task Publish_WebhookSchedule_DoesNotTouchTenantsOrMessageClient()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);

            CreateService(HttpStatusCode.OK);
            await _service.Publish(schedule.ItemId, "tenant-1");

            _tenants.Verify(t => t.GetTenantByID(It.IsAny<string>()), Times.Never);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<PublishScheduleCommand>>()), Times.Never);
        }

        [Fact]
        public async Task Publish_WebhookNonSuccess_DoesNotThrow()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);

            CreateService(HttpStatusCode.InternalServerError);
            var act = () => _service.Publish(schedule.ItemId, "tenant-1");

            await act.Should().NotThrowAsync();
            _sent.Should().HaveCount(1);
        }

        [Fact]
        public async Task Publish_WebhookNetworkFailure_DoesNotThrow()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);

            CreateService(HttpStatusCode.OK, fail: true);
            var act = () => _service.Publish(schedule.ItemId, "tenant-1");

            await act.Should().NotThrowAsync();
        }

        // ---------- Queue branch unchanged ----------

        [Fact]
        public async Task Publish_QueueSchedule_UsesConfiguredQueueName()
        {
            var schedule = new Schedule
            {
                ItemId = "sched-q",
                IsActive = true,
                TriggerType = ScheduleTriggerType.Queue,
                Queue = new QueueConfiguration { QueueName = "configured-queue" },
                Payload = "{}",
                CronExpression = "0 9 * * *",
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(1),
            };
            SetupSchedule(schedule);
            _tenants
                .Setup(t => t.GetTenantByID("tenant-1"))
                .Returns(new Tenant
                {
                    TenantId = "tenant-1",
                    DBName = "db-tenant-1",
                    DbConnectionString = "mongodb://localhost",
                    JwtTokenParameters = new JwtTokenParameters
                    {
                        IssueDate = DateTime.UtcNow,
                        PrivateCertificatePassword = "x",
                    },
                });

            CreateService(HttpStatusCode.OK);
            await _service.Publish(schedule.ItemId, "tenant-1");

            _messageClient.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<PublishScheduleCommand>>(c =>
                c.ConsumerName == "configured-queue")), Times.Once);
        }
    }
}
