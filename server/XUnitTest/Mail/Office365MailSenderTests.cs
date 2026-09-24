using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The Office 365 strategy: OAuth to Graph, password to SMTP, and a bad record to neither.
    /// </summary>
    public class Office365MailSenderTests
    {
        private readonly Mock<Office365GraphMailSender> _graph = new(
            Mock.Of<IOffice365TokenProvider>(),
            Mock.Of<IHttpClientFactory>(),
            NullLogger<Office365GraphMailSender>.Instance);

        private readonly Mock<Office365SmtpClient> _smtp = new(NullLogger<Office365SmtpClient>.Instance);

        public Office365MailSenderTests()
        {
            _graph
                .Setup(g => g.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            _smtp
                .Setup(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()))
                .ReturnsAsync(true);
        }

        private Office365MailSender Sender() =>
            new(_graph.Object, _smtp.Object, NullLogger<Office365MailSender>.Instance);

        private static MailServerConfiguration OAuthConfig() => new()
        {
            ItemId = "cfg-1",
            Provider = MailServiceProvider.Office365Smtp,
            IsInbound = false,
            AuthenticationType = MailAuthenticationType.OAuthClientCredentials,
            SecurityMode = MailSecurityMode.StartTls,
            Host = "smtp.office365.com",
            Port = 587,
            TenantId = "contoso-tenant",
            ClientId = "mailer-app",
            ClientSecretReference = "secret-1",
            MailboxAddress = "mailer@contoso.com",
            SenderName = "Contoso Notifications",
            SenderAddress = "notifications@contoso.com",
            IsEnableSnsConfiguration = false
        };

        private static MailServerConfiguration PasswordConfig()
        {
            var config = OAuthConfig();
            config.AuthenticationType = MailAuthenticationType.Password;
            config.TenantId = null;
            config.ClientId = null;
            config.ClientSecretReference = null;
            config.MailboxAddress = null;
            config.SenderUserName = "support@contoso.com";
            config.AccountPassword = "password1";
            return config;
        }

        private static MailToBeSent AMail(MailServerConfiguration config) => new()
        {
            ItemId = "mail-1",
            To = ["recipient@example.com"],
            MailServerConfiguration = config
        };

        private static MailBody ABody() => new() { Subject = "Invoice", Body = "<p>Invoice</p>", Attachments = [] };

        private static IDisposable TenantContext(string tenantId = "blocks-tenant")
        {
            var previous = BlocksContext.GetContext();
            BlocksContext.SetContext(
                BlocksContext.Create(tenantId, [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", tenantId),
                true);
            return new Restore(previous);
        }

        private sealed class Restore(BlocksContext? previous) : IDisposable
        {
            public void Dispose() => BlocksContext.SetContext(previous!, previous is not null);
        }

        private void VerifyNeitherTransport()
        {
            _graph.Verify(g => g.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>(), It.IsAny<string>()), Times.Never);
            _smtp.Verify(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()), Times.Never);
        }

        [Fact]
        public void OwnsTheOffice365Provider_AndItsOwnDiagnostics()
        {
            Sender().Provider.Should().Be(MailServiceProvider.Office365Smtp);
            Sender().EmitsOwnFailureDiagnostic.Should().BeTrue();
        }

        [Fact]
        public async Task AnOAuthRecord_GoesThroughGraph_ForTheAmbientTenant()
        {
            using var _ = TenantContext("blocks-tenant");
            var mail = AMail(OAuthConfig());

            var result = await Sender().SendAsync(mail, ABody());

            result.Should().BeTrue();
            _graph.Verify(g => g.SendAsync(mail, It.IsAny<MailBody>(), "blocks-tenant"), Times.Once);
            _smtp.Verify(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>()), Times.Never);
        }

        [Fact]
        public async Task APasswordRecord_GoesThroughSmtp()
        {
            using var _ = TenantContext();
            var mail = AMail(PasswordConfig());

            var result = await Sender().SendAsync(mail, ABody());

            result.Should().BeTrue();
            _smtp.Verify(s => s.SendAsync(mail, It.IsAny<MailBody>()), Times.Once);
            _graph.Verify(g => g.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ATransportFailure_IsReturnedAsIs()
        {
            using var _ = TenantContext();
            _graph
                .Setup(g => g.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            (await Sender().SendAsync(AMail(OAuthConfig()), ABody())).Should().BeFalse();
        }

        [Fact]
        public async Task AnInvalidRecord_FailsBeforeEitherTransport()
        {
            using var _ = TenantContext();
            var config = OAuthConfig();
            config.Port = 25;

            (await Sender().SendAsync(AMail(config), ABody())).Should().BeFalse();
            VerifyNeitherTransport();
        }

        [Fact]
        public async Task AnOAuthRecordWithoutAMailbox_FailsBeforeEitherTransport()
        {
            using var _ = TenantContext();
            var config = OAuthConfig();
            config.MailboxAddress = null;

            (await Sender().SendAsync(AMail(config), ABody())).Should().BeFalse();
            VerifyNeitherTransport();
        }

        [Fact]
        public async Task APasswordRecordWithoutAPassword_FailsBeforeEitherTransport()
        {
            using var _ = TenantContext();
            var config = PasswordConfig();
            config.AccountPassword = "";

            (await Sender().SendAsync(AMail(config), ABody())).Should().BeFalse();
            VerifyNeitherTransport();
        }

        [Fact]
        public async Task WithoutATenantContext_FailsBeforeEitherTransport()
        {
            (await Sender().SendAsync(AMail(OAuthConfig()), ABody())).Should().BeFalse();
            VerifyNeitherTransport();
        }

        [Fact]
        public async Task SnsEnabled_FailsClosedBeforeEitherTransport()
        {
            using var _ = TenantContext();
            var config = OAuthConfig();
            config.IsEnableSnsConfiguration = true;

            (await Sender().SendAsync(AMail(config), ABody())).Should().BeFalse();
            VerifyNeitherTransport();
        }
    }
}
