using System.Net;
using DomainService.Notification.Push;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using XUnitTest.Routing;
using XUnitTest.TestHelpers;
using static XUnitTest.Notification.Push.PushTestKit;

namespace XUnitTest.Notification.Push
{
    /// <summary>
    /// Test pushes end to end on local MongoDB with a fake Google: only the caller's own devices, platform rules, a
    /// result per device, expired tokens removed, the last test kept, and nothing secret in answers or logs.
    /// </summary>
    public sealed class PushTestServiceTests : IDisposable
    {
        private readonly TenantPlacementFixture _fixture = new();
        private readonly FakeGoogle _google = new();
        private readonly Mock<IPushSettingsStore> _settings = new();
        private readonly Mock<IPushCredentialStore> _credentials = new();
        private readonly Mock<IPushStatusStore> _status = new();
        private readonly Mock<IPushLocalizationRepository> _localization = new();
        private readonly CapturingLogger<FcmPushSender> _senderLogger = new();
        private readonly PushDeviceService _devices;
        private readonly PushTestService _sut;
        private PushStatusRecord? _savedStatus;

        public PushTestServiceTests()
        {
            var store = new PushDeviceStore(_fixture.Provider, _fixture.Secret);
            var factory = new FakeHttpClientFactory(_google);
            var sender = new FcmPushSender(_credentials.Object, new FcmAccessTokenProvider(factory, TimeProvider.System, new CapturingLogger<FcmAccessTokenProvider>()),
                new FcmClient(factory, TimeProvider.System), store, new NoDelay(), _senderLogger);
            var runner = new PushTestRunner(store, sender, _status.Object, TimeProvider.System, new CapturingLogger<PushTestRunner>());
            _devices = new PushDeviceService(store, TimeProvider.System);
            _sut = new PushTestService(_settings.Object, _devices, runner, _localization.Object);

            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings(web: true));
            _credentials.Setup(c => c.ReadAsync("dev", It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ServiceAccountJson());
            _status.Setup(s => s.SaveAsync(It.IsAny<PushStatusRecord>(), It.IsAny<CancellationToken>()))
                   .Callback<PushStatusRecord, CancellationToken>((r, _) => _savedStatus = r)
                   .Returns(Task.CompletedTask);
            _localization.Setup(l => l.GetTextsAsync("dev", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync(new PushKeyTexts("en-US", new Dictionary<string, IReadOnlyDictionary<string, string>>
                         {
                             ["order.title"] = new Dictionary<string, string> { ["en-US"] = "Order {{orderId}} shipped" },
                         }));
        }

        public void Dispose() => _fixture.Dispose();

        private async Task RegisterAsync(string userId, string token, string platform)
        {
            TestBlocksContext.Set("dev", userId);
            await _devices.RegisterAsync(new RegisterDeviceRequest { Token = token, Platform = platform, Name = "Phone of " + userId }, CancellationToken.None);
        }

        private Task<PushTestResult> TestAsync(string? platform = null)
        {
            TestBlocksContext.Set("dev", "alice");
            return _sut.SendTestAsync(new PushTestRequest { Platform = platform }, CancellationToken.None);
        }

        [Fact]
        public async Task ATest_GoesToTheCallersOwnDevicesOnly_WithAResultPerDevice()
        {
            await RegisterAsync("alice", "alice-android", "android");
            await RegisterAsync("alice", "alice-web", "web");
            await RegisterAsync("bob", "bob-android", "android");

            var result = await TestAsync();

            result.IsSuccess.Should().BeTrue();
            result.Delivered.Should().Be(2);
            result.Devices.Select(d => (d.Platform, d.Name, d.Result)).Should().BeEquivalentTo(new[]
            {
                ("android", "Phone of alice", PushTestCodes.Delivered),
                ("web", "Phone of alice", PushTestCodes.Delivered),
            });
            _google.FcmRequests.Select(r => r.Body).Should().NotContain(b => b.Contains("bob-android"));
        }

        [Fact]
        public async Task AnExpiredToken_IsRemoved_AndTheLastTestNamesTheReason()
        {
            await RegisterAsync("alice", "alice-android", "android");
            _google.Then(HttpStatusCode.NotFound, """{"error":{"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}""");

            var result = await TestAsync("android");

            result.Devices.Single().Should().Match<PushTestDevice>(d => d.Result == PushTestCodes.TokenExpired && d.Removed);
            (await _fixture.Dev.GetCollection<NotificationDevice>(NotificationDevice.CollectionName).CountDocumentsAsync(FilterDefinition<NotificationDevice>.Empty))
                .Should().Be(0);
            _savedStatus!.Android.Should().Match<PushLastTest>(t => t.Delivered == 0 && t.Failed == 1 && t.Reason == PushTestCodes.TokenExpired);
            _savedStatus.Ios.Should().BeNull();
        }

        [Fact]
        public async Task APlatformThatIsOff_IsRefused_AndNothingSent()
        {
            await RegisterAsync("alice", "alice-ios", "ios");
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings(ios: false));

            var result = await TestAsync("ios");

            result.Errors.Should().Equal(new Dictionary<string, string> { [PushTestResult.PlatformField] = PushTestResult.PlatformOff });
            _google.FcmRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task PushOff_IsRefused()
        {
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings(enabled: false));

            var result = await TestAsync();

            result.Errors.Should().ContainKey(PushTestResult.PushField);
        }

        [Fact]
        public async Task NoOwnDevice_IsRefused_EvenWhenOthersHaveDevices()
        {
            await RegisterAsync("bob", "bob-android", "android");

            var result = await TestAsync();

            result.Errors.Should().Equal(new Dictionary<string, string> { [PushTestResult.DevicesField] = PushTestResult.NoDevice });
            _google.FcmRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task AnUnknownPlatform_IsRefused()
        {
            var result = await TestAsync("windows");

            result.Errors.Should().ContainKey(PushTestResult.PlatformField);
        }

        [Fact]
        public async Task ASecretStoreFailure_SendsNothing_AndNeitherAnswerNorLogHoldsTheError()
        {
            await RegisterAsync("alice", "alice-android", "android");
            _credentials.Setup(c => c.ReadAsync("dev", It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("vault says s3cr3t"));

            var result = await TestAsync();

            result.IsSuccess.Should().BeFalse();
            result.Errors!.Values.Should().NotContain(v => v.Contains("s3cr3t"));
            _senderLogger.All.Should().NotContain("s3cr3t");
            _google.FcmRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task ANotificationDraft_IsSentWithSampleData_AndItsOptions()
        {
            await RegisterAsync("alice", "alice-android", "android");
            TestBlocksContext.Set("dev", "alice");

            var result = await _sut.SendNotificationTestAsync(new PushNotificationTestRequest
            {
                TitleKey = "order.title",
                BodyKey = "order.body",
                Link = "app://orders/{{orderId}}",
                Platforms = ["android"],
                Options = new PushTestOptions { Android = new AndroidPushOptions { Priority = "high", CollapseKey = "o-{orderId}" } },
            }, CancellationToken.None);

            result.Delivered.Should().Be(1);
            var body = _google.FcmRequests.Single().Body;
            body.Should().Contain("Order orderId shipped").And.Contain("order.body").And.Contain("app://orders/orderId")
                .And.Contain("\"priority\":\"HIGH\"").And.Contain("\"collapse_key\":\"o-orderId\"");
        }

        [Fact]
        public async Task ANotificationDraftWithBadOptions_IsRefused_AndNothingSent()
        {
            await RegisterAsync("alice", "alice-android", "android");
            TestBlocksContext.Set("dev", "alice");

            var result = await _sut.SendNotificationTestAsync(new PushNotificationTestRequest
            {
                TitleKey = "order.title",
                BodyKey = "",
                Options = new PushTestOptions { Android = new AndroidPushOptions { TtlSeconds = 2_419_201 } },
            }, CancellationToken.None);

            result.Errors.Should().ContainKeys("bodyKey", "android.ttlSeconds");
            _google.FcmRequests.Should().BeEmpty();
        }
    }
}
