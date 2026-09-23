using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using MailBoxSyncService.Services;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.MailBoxSyncService
{
    public class Office365ImapPollerTests
    {
        private readonly Mock<IMailBoxSyncService> _sync = new();
        private readonly Mock<IOffice365TokenProvider> _tokens = new();

        public Office365ImapPollerTests()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("an-access-token");
        }

        private Office365ImapPoller Poller() =>
            new(_sync.Object, _tokens.Object, NullLogger<Office365ImapPoller>.Instance);

        private static MailServerConfiguration Config() => new()
        {
            ItemId = "cfg-in",
            Provider = MailServiceProvider.Office365Smtp,
            IsInbound = true,
            AuthenticationType = MailAuthenticationType.OAuthClientCredentials,
            SecurityMode = MailSecurityMode.SslOnConnect,
            Host = "outlook.office365.com",
            Port = 993,
            TenantId = "contoso-tenant",
            ClientId = "mailer-app",
            ClientSecretReference = "secret-1",
            MailboxAddress = "support@contoso.com",
            IsEnableSnsConfiguration = false
        };

        [Fact]
        public async Task Poll_RequestsATokenForThePolledTenant_AndSyncsWithXoauth2()
        {
            await Poller().PollAsync(Config(), "blocks-tenant");

            _tokens.Verify(t => t.GetTokenAsync(
                It.Is<Office365TokenRequest>(r =>
                    r.BlocksTenantId == "blocks-tenant"
                    && r.EntraTenantId == "contoso-tenant"
                    && r.ClientId == "mailer-app"
                    && r.ClientSecretReference == "secret-1"),
                It.IsAny<CancellationToken>()), Times.Once);

            _sync.Verify(s => s.SyncInboxAsync(
                It.Is<MailServerConfiguration>(c => c.ItemId == "cfg-in"),
                "blocks-tenant",
                It.Is<SaslMechanism>(m => m is SaslMechanismOAuth2 && m.Credentials.UserName == "support@contoso.com")),
                Times.Once);
        }

        [Fact]
        public async Task Poll_AnInvalidRecord_TouchesNeitherTheVaultNorTheMailbox()
        {
            var config = Config();
            config.AuthenticationType = MailAuthenticationType.Password;

            await Poller().PollAsync(config, "blocks-tenant");

            _tokens.VerifyNoOtherCalls();
            _sync.VerifyNoOtherCalls();
        }
    }
}
