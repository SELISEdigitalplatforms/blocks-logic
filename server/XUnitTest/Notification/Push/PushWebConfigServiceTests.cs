using Api.Controllers;
using DomainService.Notification.Push;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using XUnitTest.TestHelpers;
using static XUnitTest.Notification.Push.PushTestKit;

namespace XUnitTest.Notification.Push
{
    /// <summary>
    /// The web config goes only to a signed-in user of the context tenant, only when push and Web are on, is read by
    /// its secret id, is never cached and never logged.
    /// </summary>
    public sealed class PushWebConfigServiceTests : IDisposable
    {
        private const string WebJson = """{"apiKey":"AIzaSyA-web-api-key-value-1234567890","appId":"1:123:web:abc","messagingSenderId":"123","vapidKey":"BVapidKeyValue"}""";

        private readonly Mock<IPushSettingsStore> _settings = new();
        private readonly Mock<IPushCredentialStore> _credentials = new();
        private readonly CapturingLogger<PushWebConfigService> _logger = new();
        private readonly PushWebConfigService _sut;

        public PushWebConfigServiceTests()
        {
            var settings = Settings(web: true);
            settings.WebSecretId = "web-secret-1";
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
            _credentials.Setup(c => c.ReadAsync("dev", "web-secret-1", PushSettingsRecord.DefaultWebSecretName, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(WebJson);
            _sut = new PushWebConfigService(_settings.Object, _credentials.Object, _logger);
            TestBlocksContext.Set("dev", "alice");
        }

        public void Dispose() => TestBlocksContext.Clear();

        [Fact]
        public async Task ASignedInUser_GetsTheContextTenantsWebConfig()
        {
            var response = await _sut.GetAsync(CancellationToken.None);

            response.Data.Should().Be(new PushWebConfig("AIzaSyA-web-api-key-value-1234567890", "1:123:web:abc", "123", "BVapidKeyValue", ProjectId));
            _logger.All.Should().NotContain("AIzaSyA");
        }

        [Fact]
        public async Task ACallerWithoutAUserId_IsRefused_AndNoSecretIsRead()
        {
            TestBlocksContext.Set("dev", "");

            var response = await _sut.GetAsync(CancellationToken.None);

            response.Should().BeSameAs(PushWebConfigResponse.NotSetUp);
            _credentials.VerifyNoOtherCalls();
        }

        public static TheoryData<PushSettingsRecord?> WebNotAvailable() => new()
        {
            null,
            Settings(enabled: false, web: true),
            Settings(web: false),
            new PushSettingsRecord { Enabled = true, KeyUploadedAt = DateTime.UtcNow, Platforms = new PushPlatformSwitches { Web = true } },
        };

        [Theory]
        [MemberData(nameof(WebNotAvailable))]
        public async Task PushOffWebOffOrNoWebConfig_IsNotSetUp(PushSettingsRecord? settings)
        {
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

            var response = await _sut.GetAsync(CancellationToken.None);

            response.Errors.Should().Equal(new Dictionary<string, string> { ["web"] = PushWebConfigResponse.NotSetUpText });
        }

        [Fact]
        public async Task ASecretStoreFailure_IsNotSetUp_AndLoggedByTypeOnly()
        {
            _credentials.Setup(c => c.ReadAsync("dev", It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new InvalidOperationException("secret firebase-web-config holds AIzaSyA"));

            var response = await _sut.GetAsync(CancellationToken.None);

            response.IsSuccess.Should().BeFalse();
            _logger.All.Should().Contain(nameof(InvalidOperationException)).And.NotContain("AIzaSyA");
        }

        [Fact]
        public async Task TheAnswer_IsNeverCached()
        {
            var controller = new NotifierPushWebConfigController(_sut)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            await controller.GetPushWebConfig();

            controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        }
    }
}
