using System.Linq.Expressions;
using System.Net;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WebPush;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Worker-side delivery for #274: 3 attempts (0s, +5s, +30s), 410 deletes and stops, final
    /// failure is logged only. Delays are recorded through the delay seam, so nothing sleeps.
    /// </summary>
    public sealed class WebPushDeliveryServiceTests : IDisposable
    {
        private readonly Mock<INotificationRepository> _repo = new();
        private readonly Mock<IWebPushVapidKeyService> _vapid = new();
        private readonly Mock<IWebPushSender> _sender = new();
        private readonly Mock<ILogger<WebPushDeliveryService>> _logger = new();
        private readonly List<TimeSpan> _delays = [];
        private readonly Queue<Func<Task>> _responses = new();
        private readonly List<BlocksContext?> _contextsSeen = [];
        private readonly WebPushDeliveryService _sut;
        private int _sends;

        public WebPushDeliveryServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(null);

            _vapid.Setup(v => v.GetVapidDetailsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(("BPublicKey", "PrivateKey", "mailto:noreply@blocks.platform"));

            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    _sends++;
                    _contextsSeen.Add(BlocksContext.GetContext());
                    return _responses.Count > 0 ? _responses.Dequeue()() : Task.CompletedTask;
                });

            _sut = new WebPushDeliveryService(
                _logger.Object,
                _repo.Object,
                _vapid.Object,
                _sender.Object,
                (delay, _) =>
                {
                    _delays.Add(delay);
                    return Task.CompletedTask;
                });
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static WebPushDeliveryCommand Command(int attempt = 1) => new()
        {
            SubscriptionEndpoint = "https://push.example/device-1",
            KeysP256dh = "p256",
            KeysAuth = "auth",
            UserId = "U1",
            TenantId = "tenant-1",
            DenormalizedPayload = "{\"title\":\"Hi\"}",
            Attempt = attempt,
        };

        private static Func<Task> Status(HttpStatusCode code) => () =>
            Task.FromException(new WebPushException(
                "push failed",
                new PushSubscription("https://push.example/device-1", "p256", "auth"),
                new HttpResponseMessage(code)));

        private static Func<Task> Throws(Exception ex) => () => Task.FromException(ex);

        private static readonly Func<Task> Ok = () => Task.CompletedTask;

        private void Respond(params Func<Task>[] responses)
        {
            foreach (var r in responses)
                _responses.Enqueue(r);
        }

        private void VerifyDeleted(Times times) =>
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>()), times);

        private void VerifyLogged(LogLevel level, Times times) =>
            _logger.Verify(l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

        [Fact]
        public async Task FirstAttemptSucceeds_NoRetryNoDelete_H2()
        {
            Respond(Ok);

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(1);
            _delays.Should().BeEmpty();
            VerifyDeleted(Times.Never());
        }

        [Fact]
        public async Task SendsTheCommandPayloadToTheCommandEndpoint_H2()
        {
            PushSubscription? sent = null;
            string? payload = null;
            VapidDetails? vapid = null;
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Callback<PushSubscription, string, VapidDetails, CancellationToken>((s, p, v, _) => { sent = s; payload = p; vapid = v; })
                .Returns(Task.CompletedTask);

            await _sut.DeliverAsync(Command());

            sent!.Endpoint.Should().Be("https://push.example/device-1");
            sent.P256DH.Should().Be("p256");
            sent.Auth.Should().Be("auth");
            payload.Should().Contain("Hi");
            vapid!.PublicKey.Should().Be("BPublicKey");
            vapid.Subject.Should().Be("mailto:noreply@blocks.platform");
        }

        [Fact]
        public async Task RetryableFailureThenSuccess_RetriesAfter5s_Example2()
        {
            Respond(Status(HttpStatusCode.ServiceUnavailable), Ok);

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(2);
            _delays.Should().Equal(TimeSpan.FromSeconds(5));
            VerifyDeleted(Times.Never());
        }

        [Fact]
        public async Task NonGoneFailureOnEveryAttempt_ThreeAttemptsWith5sThen30s_H3_C1()
        {
            Respond(Status(HttpStatusCode.InternalServerError),
                    Status(HttpStatusCode.InternalServerError),
                    Status(HttpStatusCode.InternalServerError),
                    Ok);

            var act = () => _sut.DeliverAsync(Command());

            await act.Should().NotThrowAsync();
            _sends.Should().Be(3);
            _delays.Should().Equal(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
            VerifyDeleted(Times.Never());
            VerifyLogged(LogLevel.Error, Times.Once());
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.BadRequest)]
        public async Task OtherNon2xxStatusesAreRetried_H3(HttpStatusCode code)
        {
            Respond(Status(code), Ok);

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(2);
            VerifyDeleted(Times.Never());
        }

        [Fact]
        public async Task NetworkErrorIsRetried_H3()
        {
            Respond(Throws(new HttpRequestException("connection refused")), Ok);

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(2);
        }

        [Fact]
        public async Task GoneOnFirstAttempt_DeletesAndStops_H4()
        {
            Respond(Status(HttpStatusCode.Gone), Ok);

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(1);
            _delays.Should().BeEmpty();
            VerifyDeleted(Times.Once());
        }

        [Fact]
        public async Task GoneDeletesOnlyThatUsersSubscriptionForThatEndpoint_H4()
        {
            Expression<Func<WebPushSubscription, bool>>? filter = null;
            _repo.Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>()))
                 .Callback<Expression<Func<WebPushSubscription, bool>>>(f => filter = f)
                 .Returns(Task.CompletedTask);
            Respond(Status(HttpStatusCode.Gone));

            await _sut.DeliverAsync(Command());

            var match = filter!.Compile();
            match(new WebPushSubscription { UserId = "U1", Endpoint = "https://push.example/device-1" }).Should().BeTrue();
            match(new WebPushSubscription { UserId = "U1", Endpoint = "https://push.example/other" }).Should().BeFalse();
            match(new WebPushSubscription { UserId = "U2", Endpoint = "https://push.example/device-1" }).Should().BeFalse();
        }

        [Fact]
        public async Task TimeoutThenGone_DeletesAfterAttempt2_NoAttempt3_C3_Example7()
        {
            Respond(Throws(new TaskCanceledException("timed out")), Status(HttpStatusCode.Gone), Ok);

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(2);
            _delays.Should().Equal(TimeSpan.FromSeconds(5));
            VerifyDeleted(Times.Once());
        }

        [Fact]
        public async Task GoneOnAttempt3_StillDeletes_C3()
        {
            Respond(Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.Gone));

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(3);
            VerifyDeleted(Times.Once());
            VerifyLogged(LogLevel.Error, Times.Never());
        }

        [Fact]
        public async Task ResumesFromTheCommandAttempt()
        {
            Respond(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable));

            await _sut.DeliverAsync(Command(attempt: 2));

            _sends.Should().Be(2);
            _delays.Should().Equal(TimeSpan.FromSeconds(30));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-4)]
        public async Task AttemptBelowOneIsTreatedAsOne(int attempt)
        {
            Respond(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable));

            await _sut.DeliverAsync(Command(attempt));

            _sends.Should().Be(3);
        }

        [Fact]
        public async Task AttemptAboveMaxRunsOneFinalAttempt()
        {
            Respond(Status(HttpStatusCode.ServiceUnavailable));

            await _sut.DeliverAsync(Command(attempt: 9));

            _sends.Should().Be(1);
            _delays.Should().BeEmpty();
        }

        [Fact]
        public async Task VapidLoadFailureCountsAsARetryableAttempt()
        {
            _vapid.SetupSequence(v => v.GetVapidDetailsAsync(It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("vault down"))
                  .ReturnsAsync(("BPublicKey", "PrivateKey", "mailto:noreply@blocks.platform"));

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(1);
            _delays.Should().Equal(TimeSpan.FromSeconds(5));
            _vapid.Verify(v => v.GetVapidDetailsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task VapidKeysAreLoadedOncePerDelivery()
        {
            Respond(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable), Ok);

            await _sut.DeliverAsync(Command());

            _vapid.Verify(v => v.GetVapidDetailsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task VapidAlwaysFailing_GivesUpAfterThreeAttemptsWithoutSending()
        {
            _vapid.Setup(v => v.GetVapidDetailsAsync(It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("vault down"));

            await _sut.DeliverAsync(Command());

            _sends.Should().Be(0);
            _delays.Should().HaveCount(2);
            VerifyDeleted(Times.Never());
            VerifyLogged(LogLevel.Error, Times.Once());
        }

        [Fact]
        public async Task RunsUnderAnAuthenticatedContextForTheCommandTenant()
        {
            Respond(Ok);

            await _sut.DeliverAsync(Command());

            var ctx = _contextsSeen.Should().ContainSingle().Subject;
            ctx!.TenantId.Should().Be("tenant-1");
            ctx.UserId.Should().Be("U1");
            ctx.IsAuthenticated.Should().BeTrue();
        }

        [Fact]
        public async Task RestoresTheCallersContextAfterDelivery()
        {
            var caller = BlocksContext.Create(
                "caller-tenant", null, "caller", true, null, "org-1",
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "caller-tenant");
            BlocksContext.SetContext(caller);
            BlocksContext? afterInside = null;
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    afterInside = BlocksContext.GetContext();
                    return Task.CompletedTask;
                });

            await _sut.DeliverAsync(Command());

            afterInside!.TenantId.Should().Be("tenant-1");
            afterInside.OrganizationId.Should().Be("org-1");
            BlocksContext.GetContext()!.TenantId.Should().Be("caller-tenant");
        }

        [Theory]
        [InlineData("", "tenant-1")]
        [InlineData("https://push.example/device-1", "")]
        public async Task MissingEndpointOrTenant_IsDroppedWithoutSending(string endpoint, string tenant)
        {
            var command = Command();
            command.SubscriptionEndpoint = endpoint;
            command.TenantId = tenant;

            await _sut.DeliverAsync(command);

            _sends.Should().Be(0);
            VerifyLogged(LogLevel.Warning, Times.Once());
        }

        [Fact]
        public async Task NullCommand_Throws()
        {
            var act = () => _sut.DeliverAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task CallerCancellation_StopsWithoutRetrying()
        {
            using var cts = new CancellationTokenSource();
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    _sends++;
                    cts.Cancel();
                    return Task.FromCanceled(cts.Token);
                });

            var act = () => _sut.DeliverAsync(Command(), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _sends.Should().Be(1);
        }

        [Fact]
        public async Task PassesATimeoutBoundTokenToTheSender()
        {
            CancellationToken seen = default;
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Callback<PushSubscription, string, VapidDetails, CancellationToken>((_, _, _, ct) => seen = ct)
                .Returns(Task.CompletedTask);

            await _sut.DeliverAsync(Command());

            seen.CanBeCanceled.Should().BeTrue();
        }

        [Fact]
        public async Task DefaultDelayUsesRealTime()
        {
            var sut = new WebPushDeliveryService(_logger.Object, _repo.Object, _vapid.Object, _sender.Object);
            using var cts = new CancellationTokenSource();
            Respond(Status(HttpStatusCode.ServiceUnavailable));
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cts.CancelAfter(TimeSpan.FromMilliseconds(50));
                    return Status(HttpStatusCode.ServiceUnavailable)();
                });

            var act = () => sut.DeliverAsync(Command(), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>("the 5s Task.Delay is cancelled by the caller");
        }
    }
}
