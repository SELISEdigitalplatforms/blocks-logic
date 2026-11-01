using System.Net;
using DomainService.Notification.Push;
using FluentAssertions;
using Moq;
using XUnitTest.TestHelpers;
using static XUnitTest.Notification.Push.PushTestKit;

namespace XUnitTest.Notification.Push
{
    /// <summary>
    /// FCM sending through the real token provider and client over a fake HTTP handler: refused tokens are deleted,
    /// a bare 404 is not, 429/5xx retried at most 3 times, 401 renews the token once, the project comes from the key,
    /// and nothing secret reaches the logs.
    /// </summary>
    public class FcmPushSenderTests
    {
        private const string Tenant = "tenant-1";
        private const string SecretName = "firebase-service-account";
        private static readonly PushDb Db = new(Tenant, Root: false);

        private readonly FakeGoogle _google = new();
        private readonly Mock<IPushCredentialStore> _credentials = new();
        private readonly Mock<IPushDeviceStore> _devices = new();
        private readonly NoDelay _delay = new();
        private readonly CapturingLogger<FcmPushSender> _logger = new();
        private readonly CapturingLogger<FcmAccessTokenProvider> _tokenLogger = new();
        private readonly FcmPushSender _sut;

        public FcmPushSenderTests()
        {
            _credentials.Setup(c => c.ReadAsync(Tenant, It.IsAny<string?>(), SecretName, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(ServiceAccountJson());
            var factory = new FakeHttpClientFactory(_google);
            _sut = new FcmPushSender(_credentials.Object, new FcmAccessTokenProvider(factory, TimeProvider.System, _tokenLogger),
                new FcmClient(factory, TimeProvider.System), _devices.Object, _delay, _logger);
        }

        private static PushNote Note(string token) =>
            new(Db, Device("user-1", token), "Title text", "Body text", new Dictionary<string, string> { ["notificationId"] = "n-1" });

        private async Task<PushSendSummary> SendAsync(PushSettingsRecord settings, params PushNote[] notes)
        {
            var opening = await _sut.OpenAsync(Tenant, settings, CancellationToken.None);
            return await _sut.SendAsync(opening.Session!, notes, CancellationToken.None);
        }

        private Task<PushSendSummary> SendAsync(params PushNote[] notes) => SendAsync(Settings(), notes);

        private static (int Sent, int Removed, int Failed) Counts(PushSendSummary summary) => (summary.Sent, summary.Removed, summary.Failed);

        [Fact]
        public async Task ASend_PostsOneMessagePerDevice_ToTheProjectsEndpoint_WithTheToken()
        {
            var summary = await SendAsync(Note("token-a"), Note("token-b"));

            Counts(summary).Should().Be((2, 0, 0));
            _google.FcmRequests.Should().HaveCount(2).And.OnlyContain(r =>
                r.Url == "https://fcm.googleapis.com/v1/projects/demo-project/messages:send" && r.Authorization == "Bearer access-1");
            _google.FcmRequests.Select(r => r.Body).Should().Contain(b => b.Contains("token-a") && b.Contains("Title text"));
            _google.TokenRequests.Should().ContainSingle("the access token is cached between messages");
        }

        [Fact]
        public async Task Bug_KvProjectIdWasUsed_TheProjectNowComesFromTheKey()
        {
            var settings = Settings();
            settings.ProjectId = "another-project";

            await SendAsync(settings, Note("token-a"));

            _google.FcmRequests.Single().Url.Should().Contain("/projects/demo-project/").And.NotContain("another-project");
        }

        [Fact]
        public async Task TheKey_IsReadBySecretId_WhenTheRecordHasOne()
        {
            var settings = Settings();
            settings.SecretId = "secret-42";

            await SendAsync(settings, Note("token-a"));

            _credentials.Verify(c => c.ReadAsync(Tenant, "secret-42", SecretName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, """{"error":{"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}""")]
        [InlineData(HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"INVALID_ARGUMENT"},{"@type":"type.googleapis.com/google.rpc.BadRequest","fieldViolations":[{"field":"message.token","description":"Invalid registration token"}]}]}}""")]
        [InlineData(HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT","message":"The registration token is not a valid FCM registration token"}}""")]
        public async Task ARefusedToken_DeletesTheDevice(HttpStatusCode status, string body)
        {
            _google.Then(status, body);

            var summary = await SendAsync(Note("dead-token"));

            summary.Removed.Should().Be(1);
            summary.Results.Single().Removed.Should().BeTrue();
            _devices.Verify(d => d.DeleteAsync(Db, NotificationDevice.IdOf("dead-token"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("""{"error":{"status":"NOT_FOUND","message":"Requested entity was not found."}}""")]
        public async Task Bug_ABare404DeletedTheDevice_AWrongProjectNoLongerWipesDevices(string body)
        {
            _google.Then(HttpStatusCode.NotFound, body).Then(HttpStatusCode.NotFound, body);

            var summary = await SendAsync(Note("token-a"), Note("token-b"));

            Counts(summary).Should().Be((0, 0, 2));
            _devices.Verify(d => d.DeleteAsync(It.IsAny<PushDb>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("""{"error":{"status":"INVALID_ARGUMENT","message":"Request contains an invalid argument."}}""")]
        [InlineData("""{"error":{"status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.rpc.BadRequest","fieldViolations":[{"field":"message.data[0].key","description":"Invalid data key"}]}]}}""")]
        [InlineData("not json")]
        public async Task ABadMessage_KeepsTheDevice_AndLogsOnlyTheCode(string body)
        {
            _google.Then(HttpStatusCode.BadRequest, body);

            var summary = await SendAsync(Note("good-token"));

            Counts(summary).Should().Be((0, 0, 1));
            _devices.Verify(d => d.DeleteAsync(It.IsAny<PushDb>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _logger.All.Should().Contain("device kept").And.NotContain("good-token").And.NotContain("Title text");
        }

        [Fact]
        public async Task A429_IsRetried_HonouringRetryAfter_ThenSent()
        {
            _google.Then(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(7)).Then(HttpStatusCode.ServiceUnavailable);

            var summary = await SendAsync(Note("token-a"));

            summary.Sent.Should().Be(1);
            _delay.Waits.Should().Equal(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2));
            _devices.Verify(d => d.DeleteAsync(It.IsAny<PushDb>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ServerErrors_AreRetriedThreeTimes_ThenDropped_AndTheDeviceKept()
        {
            for (var i = 0; i < 5; i++) _google.Then(HttpStatusCode.InternalServerError);

            var summary = await SendAsync(Note("token-a"));

            Counts(summary).Should().Be((0, 0, 1));
            _google.FcmRequests.Should().HaveCount(1 + FcmPushSender.MaxRetries);
            _devices.Verify(d => d.DeleteAsync(It.IsAny<PushDb>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ARetryAfterOfAnHour_IsCappedAtOneMinute()
        {
            FcmPushSender.Backoff(0, TimeSpan.FromHours(1)).Should().Be(TimeSpan.FromMinutes(1));
            FcmPushSender.Backoff(2, null).Should().Be(TimeSpan.FromSeconds(4));
        }

        [Fact]
        public async Task A401_RenewsTheAccessTokenOnce()
        {
            _google.Then(HttpStatusCode.Unauthorized);

            var summary = await SendAsync(Note("token-a"));

            summary.Sent.Should().Be(1);
            _google.TokenRequests.Should().HaveCount(2);
            _google.FcmRequests[^1].Authorization.Should().Be("Bearer access-2");
        }

        [Fact]
        public async Task AMissingOrBrokenKey_OpensNoSession_AndSendsNothing()
        {
            _credentials.Setup(c => c.ReadAsync(Tenant, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("{ broken");

            var opening = await _sut.OpenAsync(Tenant, Settings(), CancellationToken.None);

            opening.Should().Be(new FcmOpening(null, FcmOpenFailure.NoKey));
            _google.TokenRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task ARefusedAccessToken_OpensNoSession()
        {
            _google.TokenStatus = HttpStatusCode.BadRequest;

            var opening = await _sut.OpenAsync(Tenant, Settings(), CancellationToken.None);

            opening.Failure.Should().Be(FcmOpenFailure.KeyRefused);
            _google.FcmRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task ASecretStoreFailure_Throws_AndIsLoggedByTypeOnly()
        {
            _credentials.Setup(c => c.ReadAsync(Tenant, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("secret firebase-service-account value s3cr3t-part"));

            var open = () => _sut.OpenAsync(Tenant, Settings(), CancellationToken.None);

            (await open.Should().ThrowAsync<PushKeyUnavailableException>()).Which.InnerException.Should().BeNull();
            _logger.All.Should().Contain(nameof(InvalidOperationException)).And.NotContain("s3cr3t-part").And.NotContain(SecretName);
            _google.FcmRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task TheTokenRequest_UsesTheFixedGoogleUrl_NeverTheKeysOwnTokenUri()
        {
            await SendAsync(Note("token-a"));

            _google.TokenRequests.Single().Should().Contain("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer");
        }

        [Fact]
        public async Task Logs_NeverHoldTokensTextsOrTheKey()
        {
            _google.Then(HttpStatusCode.NotFound).Then(HttpStatusCode.Forbidden, """{"error":{"message":"device token-b"}}""");

            await SendAsync(Note("token-a"), Note("token-b"));

            var logs = _logger.All + _tokenLogger.All;
            logs.Should().NotContain("token-a").And.NotContain("token-b").And.NotContain("Title text")
                .And.NotContain("PRIVATE KEY").And.NotContain("access-1").And.NotContain("user-1");
        }
    }
}
