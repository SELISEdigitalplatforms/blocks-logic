using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.Entities;
using CloudConfiguration.DomainService.Captcha.Entities;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.IAM.Entities;
using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Integration
{
    [Collection("Mongo integration")]
    public class ConfigurationRepositoryIntegrationTests : IDisposable
    {
        private readonly MongoIntegrationFixture _fixture;
        private readonly ConfigurationRepository _repo;

        public ConfigurationRepositoryIntegrationTests(MongoIntegrationFixture fixture)
        {
            _fixture = fixture;
            TestBlocksContext.Set("tenant-cfg", "user-cfg");

            var provider = new Mock<IDbContextProvider>();
            Route<AuthenticationConfiguration>(provider);
            Route<CaptchaConfiguration>(provider);
            Route<IamConfiguration>(provider);
            Route<MfaConfiguration>(provider);
            Route<NotificationConfiguration>(provider);
            Route<StorageConfiguration>(provider);
            Route<MailServerConfiguration>(provider);
            Route<MailConfiguration>(provider);
            provider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>())).Returns(_fixture.Database);

            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns(MongoIntegrationFixture.ConnectionString);

            _repo = new ConfigurationRepository(provider.Object, secret.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        // Prefix collection names so parallel tests within this class do not collide.
        private void Route<T>(Mock<IDbContextProvider> provider)
        {
            provider.Setup(p => p.GetCollection<T>(It.IsAny<string>()))
                .Returns((string name) => _fixture.GetCollection<T>(Prefixed(name)));
        }

        private readonly string _ns = Guid.NewGuid().ToString("N");
        private string Prefixed(string name) => _ns + "_" + name;

        // ---------- Authentication ----------
        [Fact]
        public async Task Authentication_UpdateAndGet_RoundTrips()
        {
            var id = MongoDB.Bson.ObjectId.GenerateNewId();
            var config = new AuthenticationConfiguration { ItemId = id, AccessTokenValidForNumberMinutes = 15 };
            await _fixture.GetCollection<AuthenticationConfiguration>(Prefixed("AuthenticationConfigurations"))
                .InsertOneAsync(config);

            var updated = new AuthenticationConfiguration { ItemId = id, AccessTokenValidForNumberMinutes = 30 };
            await _repo.UpdateAuthenticationConfigAsync(updated);

            var loaded = await _repo.GetAuthenticationConfigurationAsync();
            loaded.AccessTokenValidForNumberMinutes.Should().Be(30);
        }

        // ---------- Captcha ----------
        [Fact]
        public async Task Captcha_SaveGetUpdateStatus_Works()
        {
            var cfg = new CaptchaConfiguration { ItemId = Guid.NewGuid().ToString(), Provider = "recaptcha", IsEnable = true };
            await _repo.SaveCaptchaConfigurationAsync(cfg);

            (await _repo.GetCaptchaConfigurationByProviderAsync("recaptcha")).Should().NotBeNull();
            (await _repo.GetCaptchaConfigurationByIdAsync(cfg.ItemId)).Should().NotBeNull();
            (await _repo.GetCaptchaConfigurationAsync()).Should().NotBeNull();

            var all = await _repo.GetCaptchaConfigurationsAsync(new GetCaptchaConfigurationsRequest());
            all.Configurations.Should().ContainSingle();

            await _repo.UpdateCaptchaConfigurationStatusAsync(new UpdateCaptchaConfigurationStatusRequest { ItemId = cfg.ItemId, IsEnable = true });
            (await _repo.GetCaptchaConfigurationByIdAsync(cfg.ItemId)).IsEnable.Should().BeTrue();

            await _repo.UpdateCaptchaConfigurationStatusAsync(new UpdateCaptchaConfigurationStatusRequest { ItemId = cfg.ItemId, IsEnable = false });
            (await _repo.GetCaptchaConfigurationByIdAsync(cfg.ItemId)).IsEnable.Should().BeFalse();
        }

        // ---------- IAM ----------
        [Fact]
        public async Task Iam_SaveAndGet_RoundTrips()
        {
            var cfg = new IamConfiguration { ItemId = MongoDB.Bson.ObjectId.GenerateNewId(), AccountActivationUrl = "https://a" };
            await _repo.SaveIamConfigurationAsync(cfg);

            var loaded = await _repo.GetIamConfigurationAsync();
            loaded.Should().NotBeNull();
            loaded.AccountActivationUrl.Should().Be("https://a");
        }

        // ---------- MFA ----------
        [Fact]
        public async Task Mfa_GetDefault_ReturnsInserted()
        {
            var cfg = new MfaConfiguration { ItemId = Guid.NewGuid().ToString(), EnableMfa = true };
            await _fixture.GetCollection<MfaConfiguration>(Prefixed("MfaConfigurations")).InsertOneAsync(cfg);

            var loaded = await _repo.GetDefaultMfaConfiguration();
            loaded.EnableMfa.Should().BeTrue();
        }

        // ---------- Notification ----------
        [Fact]
        public async Task Notification_SaveGetListDelete_Works()
        {
            var cfg = new NotificationConfiguration { ItemId = Guid.NewGuid().ToString(), Name = "n1" };
            await _repo.SaveNotificationConfigurationAsync(cfg);

            (await _repo.GetNotificationConfigurationByIdAsync(cfg.ItemId)).Should().NotBeNull();
            (await _repo.GetNotificationConfigurationByNameAsync("n1")).Should().NotBeNull();

            var list = await _repo.GetNotificationConfigurationsAsync(new GetNotificationConfigurationsRequest { PageSize = 10, Page = 0 });
            list.TotalCount.Should().BeGreaterThanOrEqualTo(1);

            await _repo.DeleteNotificationConfigurationAsync(new DeleteNotificatoinConfigurationRequest { ItemId = cfg.ItemId });
            (await _repo.GetNotificationConfigurationByIdAsync(cfg.ItemId)).Should().BeNull();
        }

        // ---------- Storage ----------
        [Fact]
        public async Task Storage_SaveGetListStrategyDelete_Works()
        {
            var cfg = new StorageConfiguration { ItemId = Guid.NewGuid().ToString(), Name = "s1", StorageStrategy = "Azure" };
            await _repo.SaveStorageConfigurationAsync(cfg);

            (await _repo.GetStorageConfigurationByNameAsync("s1")).Should().NotBeNull();
            (await _repo.GetStorageConfigurationByIdAsync(cfg.ItemId)).Should().NotBeNull();
            (await _repo.GetStorageConfigurationStrategyAsync("Azure")).Should().NotBeNull();
            (await _repo.GetAllStorageConfigurationsByDateAsync()).Should().NotBeEmpty();

            await _repo.DeleteStorageConfigurationByNameAsync("s1");
            (await _repo.GetStorageConfigurationByNameAsync("s1")).Should().BeNull();
        }

        // ---------- Mail ----------
        [Fact]
        public async Task Mail_SaveGetListDelete_Works()
        {
            var cfg = new MailServerConfiguration { ItemId = Guid.NewGuid().ToString(), Name = "m1" };
            await _repo.SaveMailConfigurationAsync(cfg);

            (await _repo.GetMailConfigurationByIdAsync(cfg.ItemId)).Should().NotBeNull();
            (await _repo.GetAllMailConfigurationsAsync()).Should().NotBeEmpty();

            await _repo.DeleteMailConfigurationAsync(cfg.ItemId);
            (await _repo.GetMailConfigurationByIdAsync(cfg.ItemId)).Should().BeNull();
        }

        // ---------- Generic Upsert ----------
        [Fact]
        public async Task UpsertAsync_InsertsAndUpdates()
        {
            var cfg = new StorageConfiguration { ItemId = Guid.NewGuid().ToString(), Name = "up1", StorageStrategy = "AWS" };

            // No explicit collection name: UpsertAsync derives typeof(T).Name + "s"
            // = "StorageConfigurations", which the router prefixes just like the
            // other StorageConfiguration methods, so the read below sees the write.
            await _repo.UpsertAsync(cfg, c => c.ItemId == cfg.ItemId);

            var loaded = await _repo.GetStorageConfigurationByIdAsync(cfg.ItemId);
            loaded.Should().NotBeNull();
            loaded.Name.Should().Be("up1");
        }
    }
}
