using System.Net;
using System.Text;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;
using Blocks.Genesis.Entities.Logic;

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
        private readonly Mock<IScheduleExecutionRecorder> _recorder = new();
        private readonly List<CapturedRequest> _sent = [];
        private readonly List<ScheduleExecution> _recorded = [];
        // Ordered trace of clock reads, sends and recorder calls: proves the row is written after delivery.
        private readonly List<string> _events = [];
        private readonly StepClock _clock;
        private SchedulePublisherService _service;
        public SchedulePublisherServiceTests()
        {
            _clock = new StepClock(_events);
            _recorder.Setup(r => r.RecordAsync(It.IsAny<ScheduleExecution>()))
                .Callback<ScheduleExecution>(row => { _events.Add("record"); _recorded.Add(row); })
                .Returns(Task.CompletedTask);
            _service = CreateService(HttpStatusCode.OK);
        }

        public void Dispose()
        {
            _service.Dispose();
            BlocksContext.ClearContext();
        }

        private SchedulePublisherService CreateService(
            HttpStatusCode code,
            bool fail = false,
            Func<HttpResponseMessage>? respond = null,
            Exception? throws = null,
            bool snapshotFails = false)
        {
            _service?.Dispose();
            var handler = new StubHandler(code, _sent, fail, respond, throws, _events);
            var service = new TestableSchedulePublisherService(
                handler,
                Mock.Of<ILogger<SchedulePublisherService>>(),
                _scheduleRepository.Object,
                _messageClient.Object,
                _tenants.Object,
                _recorder.Object,
                _clock,
                snapshotFails);
            _service = service;
            return service;
        }

        private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? Body, string? ContentType, List<(string Name, string Value)> Headers);

        private sealed class StubHandler(
            HttpStatusCode code,
            List<CapturedRequest> seen,
            bool fail,
            Func<HttpResponseMessage>? respond = null,
            Exception? throws = null,
            List<string>? events = null) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Snapshot before the caller disposes the request — assertions run post-send.
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                var contentType = request.Content?.Headers.ContentType?.MediaType;
                var headers = request.Headers.SelectMany(h => h.Value.Select(v => (h.Key, v))).ToList();
                seen.Add(new CapturedRequest(request.Method, request.RequestUri, body, contentType, headers));
                events?.Add("send");
                if (fail) throw new HttpRequestException("network down");
                if (throws is not null) throw throws;
                return respond?.Invoke() ?? new HttpResponseMessage(code);
            }
        }

        /// <summary>A clock that moves one second per read, so fired and finished times differ.</summary>
        private sealed class StepClock(List<string> events) : TimeProvider
        {
            public DateTimeOffset Now { get; private set; } = new(2026, 10, 26, 10, 5, 0, TimeSpan.Zero);
            public List<DateTime> Reads { get; } = [];

            public override DateTimeOffset GetUtcNow()
            {
                Now = Now.AddSeconds(1);
                Reads.Add(Now.UtcDateTime);
                events.Add("clock");
                return Now;
            }
        }

        private sealed class TestableSchedulePublisherService(
            HttpMessageHandler handler,
            ILogger<SchedulePublisherService> logger,
            IScheduleRepository scheduleRepository,
            IMessageClient messageClient,
            ITenants tenants,
            IScheduleExecutionRecorder recorder,
            TimeProvider timeProvider,
            bool snapshotFails) : SchedulePublisherService(logger, scheduleRepository, messageClient, tenants, recorder, timeProvider)
        {
            // Exposes the internal HTTP client construction for testing by replacing the
            // client's handler through the protected hook below.
            public HttpClient TestClient { get; } = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            protected override HttpClient CreateHttpClient() => TestClient;

            protected override string BuildSnapshot(Schedule schedule) =>
                snapshotFails ? throw new InvalidOperationException("snapshot boom") : base.BuildSnapshot(schedule);
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

        // ---------- Date window ----------

        public static TheoryData<int?, int?, bool> DateWindowCases => new()
        {
            // startOffsetDays, endOffsetDays (null = not set), shouldFire
            { null, null, true },   // no dates: live from creation, never ends
            { null, 1, true },
            { -1, null, true },
            { -1, 1, true },
            { 1, null, false },     // not started yet
            { null, -1, false },    // already ended
            { 1, 2, false },
            { -2, -1, false },
        };

        [Theory]
        [MemberData(nameof(DateWindowCases))]
        public async Task Publish_RespectsOptionalDateWindow(int? startOffsetDays, int? endOffsetDays, bool shouldFire)
        {
            var schedule = WebhookSchedule();
            schedule.StartDate = startOffsetDays is null ? null : DateTime.UtcNow.AddDays(startOffsetDays.Value);
            schedule.EndDate = endOffsetDays is null ? null : DateTime.UtcNow.AddDays(endOffsetDays.Value);
            SetupSchedule(schedule);

            CreateService(HttpStatusCode.OK);
            await _service.Publish(schedule.ItemId, "tenant-1");

            _sent.Should().HaveCount(shouldFire ? 1 : 0);
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

            string? publishedTenant = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<PublishScheduleCommand>>()))
                .Callback(() => publishedTenant = BlocksContext.GetContext()?.TenantId)
                .Returns(Task.CompletedTask);
            XUnitTest.TestHelpers.TestBlocksContext.Set("unrelated-worker-owner");
            CreateService(HttpStatusCode.OK);
            await _service.Publish(schedule.ItemId, "tenant-1");

            Assert.Equal("tenant-1", publishedTenant);
            _scheduleRepository.Verify(r => r.GetByIdAsync(schedule.ItemId, "tenant-1"), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<PublishScheduleCommand>>(c =>
                c.ConsumerName == "configured-queue")), Times.Once);
        }

        // ---------- Execution history (SPEC5 §5.2-§5.3) ----------

        private Schedule QueueSchedule(string? queueName = "configured-queue", ScheduleKind kind = ScheduleKind.Application)
        {
            _tenants
                .Setup(t => t.GetTenantByID("tenant-1"))
                .Returns(new Tenant
                {
                    TenantId = "tenant-1",
                    DBName = "db-tenant-1",
                    DbConnectionString = "mongodb://localhost",
                    JwtTokenParameters = new JwtTokenParameters { IssueDate = DateTime.UtcNow, PrivateCertificatePassword = "x" },
                });
            return new Schedule
            {
                ItemId = "sched-q",
                Name = "Queue schedule",
                IsActive = true,
                Kind = kind,
                TriggerType = ScheduleTriggerType.Queue,
                Queue = queueName is null ? null : new QueueConfiguration { QueueName = queueName },
                Payload = "{}",
                CronExpression = "0 9 * * *",
            };
        }

        private ScheduleExecution SingleRow() => _recorded.Should().ContainSingle().Subject;

        private static int StatusOf(ScheduleExecution row) => (int)row.Response![ScheduleExecution.ResponseStatusKey];

        private static string? BodyOf(ScheduleExecution row) =>
            row.Response!.TryGetValue(ScheduleExecution.ResponseBodyKey, out var body) ? (string)body : null;

        [Fact]
        public async Task Record_WebhookResponse_StoresStatusAsIntAndBody_AfterDelivery()
        {
            var schedule = WebhookSchedule();
            schedule.Name = "Nightly sync";
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, respond: () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"),
            });

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            row.ScheduleId.Should().Be("sched-1");
            row.TenantId.Should().Be("tenant-1");
            row.ScheduleName.Should().Be("Nightly sync");
            Guid.TryParseExact(row.ItemId, "N", out _).Should().BeTrue();
            row.Response![ScheduleExecution.ResponseStatusKey].Should().BeOfType<int>().And.Be(200);
            BodyOf(row).Should().Be("{\"ok\":true}");
            row.Response.Keys.Should().BeEquivalentTo(ScheduleExecution.ResponseStatusKey, ScheduleExecution.ResponseBodyKey);

            // Fired is the clock read right before the send, finished the one right after; the row comes last.
            _events.TakeLast(4).Should().Equal("clock", "send", "clock", "record");
            row.FiredUtc.Should().Be(_clock.Reads[^2]);
            row.FinishedUtc.Should().Be(_clock.Reads[^1]);
            row.FiredUtc.Should().BeBefore(row.FinishedUtc!.Value);
        }

        [Fact]
        public async Task Record_TargetErrorStatus_StoresTargetCode()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, respond: () => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom"),
            });

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            StatusOf(row).Should().Be(500);
            BodyOf(row).Should().Be("boom");
        }

        [Fact]
        public async Task Record_EmptyContent_HasNoBodyKey()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.NoContent);

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            StatusOf(row).Should().Be(204);
            row.Response!.Should().NotContainKey(ScheduleExecution.ResponseBodyKey);
        }

        [Fact]
        public async Task Record_WebhookNotConfigured_Stores422_WithFiredEqualFinished()
        {
            var schedule = WebhookSchedule();
            schedule.Webhook = null;
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK);

            await _service.Publish(schedule.ItemId, "tenant-1");

            _sent.Should().BeEmpty();
            var row = SingleRow();
            StatusOf(row).Should().Be(422);
            BodyOf(row).Should().Be("Webhook is not configured.");
            row.FiredUtc.Should().Be(row.FinishedUtc);
        }

        [Fact]
        public async Task Record_NetworkError_Stores502_AndDoesNotThrow()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, fail: true);

            await _service.Invoking(s => s.Publish(schedule.ItemId, "tenant-1")).Should().NotThrowAsync();

            var row = SingleRow();
            StatusOf(row).Should().Be(502);
            BodyOf(row).Should().Be("Network error: network down");
        }

        [Fact]
        public async Task Record_Timeout_Stores504_AndDoesNotThrow()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, throws: new TaskCanceledException("timed out"));

            await _service.Invoking(s => s.Publish(schedule.ItemId, "tenant-1")).Should().NotThrowAsync();

            var row = SingleRow();
            StatusOf(row).Should().Be(504);
            BodyOf(row).Should().Be("Timed out: no response within 30 s.");
        }

        [Fact]
        public async Task Record_Cancelled_Stores503_ThenRethrows()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, throws: new OperationCanceledException(new CancellationToken(canceled: true)));

            await _service.Invoking(s => s.Publish(schedule.ItemId, "tenant-1")).Should().ThrowAsync<OperationCanceledException>();

            var row = SingleRow();
            StatusOf(row).Should().Be(503);
            BodyOf(row).Should().Be("Cancelled: the scheduler was stopping.");
        }

        [Fact]
        public async Task Record_UnexpectedError_Stores500_AndDoesNotThrow()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, throws: new InvalidOperationException("odd"));

            await _service.Invoking(s => s.Publish(schedule.ItemId, "tenant-1")).Should().NotThrowAsync();

            var row = SingleRow();
            StatusOf(row).Should().Be(500);
            BodyOf(row).Should().Be("Unexpected error: InvalidOperationException: odd");
        }

        [Fact]
        public async Task Record_BodyReadFailure_StoresStatusAndPlaceholder()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, respond: () =>
            {
                // Buffers fine inside SendAsync, but ReadAsStringAsync rejects the unknown charset.
                var content = new ByteArrayContent("hello"u8.ToArray());
                content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=not-a-charset");
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = content };
            });

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            StatusOf(row).Should().Be(202);
            BodyOf(row).Should().Be("(response body could not be read)");
        }

        [Fact]
        public async Task Record_LargeBody_IsTruncatedTo16KbWithMarker()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            var large = new string('a', 40_000);
            CreateService(HttpStatusCode.OK, respond: () => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent(large),
            });

            await _service.Publish(schedule.ItemId, "tenant-1");

            BodyOf(SingleRow()).Should().Be(new string('a', 16_384) + "\n…[truncated]");
        }

        [Fact]
        public void LimitBody_CutsAtCharacterBoundary_ForMultiByteCharacters()
        {
            // 16,383 one-byte chars then a 3-byte '€': the euro would cross the cap, so it is dropped whole.
            var euro = new string('a', 16_383) + "€€";
            SchedulePublisherService.LimitBody(euro).Should().Be(new string('a', 16_383) + "\n…[truncated]");

            // A surrogate pair (4 bytes) at the boundary is never split.
            var emoji = new string('a', 16_382) + "😀";
            Encoding.UTF8.GetByteCount(emoji).Should().Be(16_386);
            SchedulePublisherService.LimitBody(emoji).Should().Be(new string('a', 16_382) + "\n…[truncated]");

            var exact = new string('a', 16_380) + "😀";
            SchedulePublisherService.LimitBody(exact).Should().Be(exact);

            SchedulePublisherService.LimitBody("").Should().BeNull();
            SchedulePublisherService.LimitBody(null).Should().BeNull();
        }

        [Fact]
        public async Task Record_QueueSendOk_Stores202()
        {
            var schedule = QueueSchedule();
            SetupSchedule(schedule);
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<PublishScheduleCommand>>()))
                .Callback(() => _events.Add("send"))
                .Returns(Task.CompletedTask);

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            StatusOf(row).Should().Be(202);
            BodyOf(row).Should().Be("Sent to queue configured-queue.");
            _events.TakeLast(4).Should().Equal("clock", "send", "clock", "record");
        }

        [Fact]
        public async Task Record_QueueNotConfigured_Stores422()
        {
            var schedule = QueueSchedule(queueName: null);
            SetupSchedule(schedule);

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            StatusOf(row).Should().Be(422);
            BodyOf(row).Should().Be("Queue is not configured.");
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<PublishScheduleCommand>>()), Times.Never);
        }

        [Fact]
        public async Task Record_QueueSendThrows_Stores503_ThenRethrowsOriginal()
        {
            var schedule = QueueSchedule();
            SetupSchedule(schedule);
            var original = new InvalidOperationException("bus down");
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<PublishScheduleCommand>>())).ThrowsAsync(original);

            var thrown = await _service.Invoking(s => s.Publish(schedule.ItemId, "tenant-1")).Should().ThrowAsync<InvalidOperationException>();

            thrown.Which.Should().BeSameAs(original);
            var row = SingleRow();
            StatusOf(row).Should().Be(503);
            BodyOf(row).Should().Be("Queue send failed: bus down");
        }

        [Fact]
        public async Task Record_InternalSchedule_WritesNoRow_ButStillDelivers()
        {
            var schedule = QueueSchedule(kind: ScheduleKind.Internal);
            SetupSchedule(schedule);

            await _service.Publish(schedule.ItemId, "tenant-1");

            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<PublishScheduleCommand>>()), Times.Once);
            _recorded.Should().BeEmpty();
        }

        [Fact]
        public async Task Record_InactiveOrOutOfWindowOrMissing_WritesNoRow()
        {
            var inactive = WebhookSchedule();
            inactive.IsActive = false;
            SetupSchedule(inactive);
            await _service.Publish(inactive.ItemId, "tenant-1");

            var notStarted = WebhookSchedule();
            notStarted.ItemId = "sched-later";
            notStarted.StartDate = DateTime.UtcNow.AddDays(1);
            SetupSchedule(notStarted);
            await _service.Publish(notStarted.ItemId, "tenant-1");

            await _service.Publish("missing", "tenant-1");

            _sent.Should().BeEmpty();
            _recorded.Should().BeEmpty();
        }

        [Fact]
        public async Task Record_SnapshotIsRedacted()
        {
            var schedule = WebhookSchedule(headers: new Dictionary<string, string> { ["Authorization"] = "Bearer abc" }, signingSecret: "s3cret");
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK);

            await _service.Publish(schedule.ItemId, "tenant-1");

            var snapshot = SingleRow().Snapshot;
            snapshot.Should().Contain("\"Authorization\":\"[redacted]\"").And.Contain("\"signingSecret\":\"[redacted]\"");
            snapshot.Should().NotContain("Bearer abc").And.NotContain("s3cret");
        }

        [Fact]
        public async Task Record_SnapshotFailure_StillWritesRow_WithNullSnapshot()
        {
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            CreateService(HttpStatusCode.OK, snapshotFails: true);

            await _service.Publish(schedule.ItemId, "tenant-1");

            var row = SingleRow();
            row.Snapshot.Should().BeNull();
            StatusOf(row).Should().Be(200);
        }

        [Fact]
        public async Task Record_RecorderFailure_IsSwallowed_DeliveryUnchanged()
        {
            // The real recorder over a failing insert: Publish completes as if history did not exist.
            var repository = new Mock<global::Scheduler.DomainService.Repositories.IScheduleExecutionRepository>();
            repository.Setup(r => r.InsertAsync(It.IsAny<ScheduleExecution>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("mongo down"));
            var recorder = new ScheduleExecutionRecorder(
                repository.Object,
                Microsoft.Extensions.Options.Options.Create(new global::Scheduler.DomainService.Utils.ScheduleExecutionHistoryOptions()),
                Mock.Of<ILogger<ScheduleExecutionRecorder>>());
            var schedule = WebhookSchedule();
            SetupSchedule(schedule);
            using var service = new TestableSchedulePublisherService(
                new StubHandler(HttpStatusCode.OK, _sent, fail: false),
                Mock.Of<ILogger<SchedulePublisherService>>(), _scheduleRepository.Object, _messageClient.Object, _tenants.Object,
                recorder, _clock, snapshotFails: false);

            await service.Invoking(s => s.Publish(schedule.ItemId, "tenant-1")).Should().NotThrowAsync();
            _sent.Should().ContainSingle();
            repository.Verify(r => r.InsertAsync(It.IsAny<ScheduleExecution>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
