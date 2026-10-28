using Blocks.Secrets;
using DomainService.Notification.Push;
using FluentAssertions;
using Moq;
using XUnitTest.TestHelpers;
using static XUnitTest.Notification.Push.PushTestKit;

namespace XUnitTest.Notification.Push
{
    /// <summary>Push settings: key validation, the key only in Blocks Secrets (never in KV, answers or logs), and the test send.</summary>
    public sealed class PushSettingsServiceTests : IDisposable
    {
        private readonly Mock<IPushSettingsStore> _store = new();
        private readonly Mock<IPushCredentialStore> _credentials = new();
        private readonly Mock<IPushDeviceService> _devices = new();
        private readonly Mock<IPushTestSender> _testSender = new();
        private readonly CapturingLogger<PushSettingsService> _logger = new();
        private readonly PushSettingsService _sut;
        private PushSettingsRecord? _saved;

        public PushSettingsServiceTests()
        {
            TestBlocksContext.Set("dev", "admin-7");
            _store.Setup(s => s.SaveAsync(It.IsAny<PushSettingsRecord>(), It.IsAny<CancellationToken>()))
                  .Callback<PushSettingsRecord, CancellationToken>((r, _) => _saved = r)
                  .Returns(Task.CompletedTask);
            _devices.Setup(d => d.Caller()).Returns((new PushDb("dev", false), "admin-7"));
            _sut = new PushSettingsService(_store.Object, _credentials.Object, _devices.Object, _testSender.Object, TimeProvider.System, _logger);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private Task<Blocks.Genesis.BaseResponse> SaveAsync(string? projectId, bool enabled, string? json) =>
            _sut.SaveAsync(new SavePushSettingsRequest { ProjectId = projectId, Enabled = enabled, ServiceAccountJson = json }, CancellationToken.None);

        [Fact]
        public async Task AValidKey_GoesToBlocksSecrets_AndTheKvKeepsOnlyWhoAndWhen()
        {
            var json = ServiceAccountJson();

            var result = await SaveAsync(ProjectId, true, json);

            result.IsSuccess.Should().BeTrue();
            _credentials.Verify(c => c.WriteAsync("firebase-service-account", json, It.IsAny<CancellationToken>()), Times.Once);
            _saved!.ProjectId.Should().Be(ProjectId);
            _saved.Enabled.Should().BeTrue();
            _saved.KeyUploadedBy.Should().Be("admin-7");
            _saved.KeyUploadedAt.Should().NotBeNull();
            System.Text.Json.JsonSerializer.Serialize(_saved).Should().NotContain("PRIVATE KEY");
        }

        public static TheoryData<string, string> BadKeys() => new()
        {
            { "{ not json", "not valid JSON" },
            { ServiceAccountJson(type: "authorized_user"), "type service_account" },
            { ServiceAccountJson(privateKey: "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----"), "private_key" },
            { ServiceAccountJson() + new string(' ', ServiceAccountKey.MaxJsonBytes), "20 KB" },
        };

        [Theory]
        [MemberData(nameof(BadKeys))]
        public async Task ABadKey_IsRefused_WithoutEchoingIt(string json, string expected)
        {
            var result = await SaveAsync(ProjectId, true, json);

            result.IsSuccess.Should().BeFalse();
            result.Errors!["serviceAccountJson"].Should().Contain(expected);
            string.Join(" ", result.Errors.Values).Should().NotContain("PRIVATE KEY").And.NotContain(ClientEmail);
            _credentials.Verify(c => c.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _saved.Should().BeNull();
        }

        [Fact]
        public async Task AKeyOfAnotherProject_IsRefused()
        {
            var result = await SaveAsync("other-project", true, ServiceAccountJson());

            result.Errors!["projectId"].Should().Contain("another Firebase project");
            _saved.Should().BeNull();
        }

        [Fact]
        public async Task TurningPushOnWithoutAKey_IsRefused()
        {
            var result = await SaveAsync(ProjectId, true, null);

            result.Errors.Should().ContainKey("serviceAccountJson");
        }

        [Fact]
        public async Task ChangingTheProjectWithoutANewKey_IsRefused()
        {
            _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings());

            var result = await SaveAsync("new-project", true, null);

            result.Errors.Should().ContainKey("serviceAccountJson");
        }

        [Fact]
        public async Task TurningPushOff_KeepsTheStoredKeyInfo()
        {
            _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings());

            var result = await SaveAsync(ProjectId, false, null);

            result.IsSuccess.Should().BeTrue();
            _saved!.Enabled.Should().BeFalse();
            _saved.KeyUploadedBy.Should().Be("admin-1");
        }

        [Fact]
        public async Task ASecretStoreFailure_IsAnsweredAndLoggedByTypeOnly()
        {
            _credentials.Setup(c => c.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new SecretAccessDeniedException("denied for firebase-service-account"));

            var result = await SaveAsync(ProjectId, true, ServiceAccountJson());

            result.IsSuccess.Should().BeFalse();
            _saved.Should().BeNull();
            _logger.All.Should().NotContain("PRIVATE KEY").And.NotContain("denied for");
        }

        [Fact]
        public async Task Get_NeverReturnsTheKey()
        {
            _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings());

            var view = await _sut.GetAsync(CancellationToken.None);

            view.Should().Be(new PushSettingsView(ProjectId, true, true, Settings().KeyUploadedAt, "admin-1"));
        }

        [Fact]
        public async Task Test_WithoutDevices_SaysSo_AndWhenOff_SendsNothing()
        {
            _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
            _testSender.Setup(t => t.SendAsync(It.IsAny<PushDb>(), "admin-7", It.IsAny<PushSettingsRecord>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((int?)null);

            (await _sut.SendTestAsync(CancellationToken.None)).Errors.Should().ContainKey("devices");

            _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings(enabled: false));
            (await _sut.SendTestAsync(CancellationToken.None)).Errors.Should().ContainKey("push");
            _testSender.Verify(t => t.SendAsync(It.IsAny<PushDb>(), It.IsAny<string>(), It.IsAny<PushSettingsRecord>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
