using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// H3, H4, H5 and C1–C4: the Office 365 session, and every failure path failing closed.
    /// </summary>
    public class Office365SmtpClientTests
    {
        private const string Token = "an-access-token";

        private readonly Mock<IOffice365TokenProvider> _tokens = new();
        private readonly RecordingSession _session = new();

        public Office365SmtpClientTests()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Token);
        }

        private TestableOffice365SmtpClient Client() => new(_tokens.Object, _session);

        private static MailServerConfiguration Config() => new()
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

        private static MailToBeSent AMail() => new()
        {
            ItemId = "mail-1",
            CorrelationId = "corr-1",
            To = ["recipient@example.com"],
            MailServerConfiguration = Config()
        };

        private static MailBody ABody() => new()
        {
            Subject = "Invoice",
            Body = "<p>Invoice</p>",
            Attachments = []
        };

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

        // ---------- H3 / H4 / H5 ----------

        [Fact]
        public async Task Send_ConnectsWithStartTls_AuthenticatesWithXoauth2_AndSendsOnce()
        {
            using var _ = TenantContext();

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeTrue();
            _session.ConnectedHost.Should().Be("smtp.office365.com");
            _session.ConnectedPort.Should().Be(587);
            _session.SocketOptions.Should().Be(SecureSocketOptions.StartTls);
            _session.Mechanism.Should().BeOfType<SaslMechanismOAuth2>();
            _session.Mechanism!.Credentials.UserName.Should().Be("mailer@contoso.com");
            _session.SendCount.Should().Be(1);
            _session.Disconnected.Should().BeTrue();

            // The password overloads belong to the legacy providers and must never be reached.
            _session.UsedPasswordConnect.Should().BeFalse();
            _session.UsedPasswordAuthenticate.Should().BeFalse();
        }

        [Fact]
        public async Task Send_PasswordRecord_UsesStartTlsAndTheMailboxPassword_WithoutAToken()
        {
            using var _ = TenantContext();

            var mail = AMail();
            var config = mail.MailServerConfiguration;
            config.AuthenticationType = MailAuthenticationType.Password;
            config.TenantId = null;
            config.ClientId = null;
            config.ClientSecretReference = null;
            config.MailboxAddress = null;
            config.SenderUserName = "support@contoso.com";
            config.AccountPassword = "password1";

            var result = await Client().SendAsync(mail, ABody());

            result.Should().BeTrue();
            _session.ConnectedHost.Should().Be("smtp.office365.com");
            _session.SocketOptions.Should().Be(SecureSocketOptions.StartTls);
            _session.UsedPasswordAuthenticate.Should().BeTrue();
            _session.Mechanism.Should().BeNull();
            _session.SendCount.Should().Be(1);
            _tokens.Verify(
                t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Send_PasswordRecordWithoutAPassword_FailsClosedBeforeConnecting()
        {
            using var _ = TenantContext();

            var mail = AMail();
            mail.MailServerConfiguration.AuthenticationType = MailAuthenticationType.Password;
            mail.MailServerConfiguration.SenderUserName = "support@contoso.com";
            mail.MailServerConfiguration.AccountPassword = "";

            var result = await Client().SendAsync(mail, ABody());

            result.Should().BeFalse();
            _session.ConnectedHost.Should().BeNull();
        }

        [Fact]
        public async Task Send_RequestsTheTokenForTheConfigurationsOwnTenantAndApplication()
        {
            using var _ = TenantContext("blocks-tenant");

            await Client().SendAsync(AMail(), ABody());

            _tokens.Verify(
                t => t.GetTokenAsync(
                    It.Is<Office365TokenRequest>(r =>
                        r.BlocksTenantId == "blocks-tenant"
                        && r.EntraTenantId == "contoso-tenant"
                        && r.ClientId == "mailer-app"
                        && r.ClientSecretReference == "secret-1"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Send_NeverAddsSesHeaders()
        {
            using var _ = TenantContext();

            await Client().SendAsync(AMail(), ABody());

            var headers = _session.Sent!.Headers.Select(h => h.Field).ToList();
            headers.Should().NotContain("X-SES-CONFIGURATION-SET");
            headers.Should().NotContain("X-Tenant-Id");
            headers.Should().NotContain("X-Mail-Body");
        }

        [Fact]
        public async Task Send_ComposesTheSameMessageTheLegacySenderWould()
        {
            using var _ = TenantContext();

            await Client().SendAsync(AMail(), ABody());

            _session.Sent!.Subject.Should().Be("Invoice");
            _session.Sent.From.Mailboxes.Single().Address.Should().Be("notifications@contoso.com");
            _session.Sent.From.Mailboxes.Single().Name.Should().Be("Contoso Notifications");
            _session.Sent.To.Mailboxes.Single().Address.Should().Be("recipient@example.com");
        }

        [Fact]
        public async Task Send_DisconnectFailureAfterAcceptance_KeepsTheSuccessAndDoesNotResend()
        {
            using var _ = TenantContext();
            _session.ThrowOnDisconnect = new IOException("connection reset");

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeTrue("the message was already accepted; a failed goodbye must not invite a resend");
            _session.SendCount.Should().Be(1);
        }

        // ---------- C1 ----------

        [Fact]
        public async Task Send_InvalidConfiguration_FailsBeforeTheSecretAndTheSocket()
        {
            using var _ = TenantContext();
            var mail = AMail();
            mail.MailServerConfiguration.Port = 25;

            var result = await Client().SendAsync(mail, ABody());

            result.Should().BeFalse();
            _tokens.Verify(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _session.ConnectedHost.Should().BeNull();
        }

        [Fact]
        public async Task Send_WithoutATenantContext_FailsBeforeTheSecret()
        {
            var mail = AMail();

            var result = await Client().SendAsync(mail, ABody());

            result.Should().BeFalse();
            _tokens.Verify(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ---------- C4 ----------

        [Fact]
        public async Task Send_SnsEnabled_FailsClosedWithoutConnecting()
        {
            using var _ = TenantContext();
            var mail = AMail();
            mail.MailServerConfiguration.IsEnableSnsConfiguration = true;

            var result = await Client().SendAsync(mail, ABody());

            result.Should().BeFalse();
            _session.ConnectedHost.Should().BeNull();
        }

        // ---------- C2 ----------

        [Fact]
        public async Task Send_SecretResolutionFailure_MakesNoSmtpConnection()
        {
            using var _ = TenantContext();
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretNotFoundException("secret-1"));

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeFalse();
            _session.ConnectedHost.Should().BeNull();
        }

        [Fact]
        public async Task Send_VaultOutage_MakesNoSmtpConnection()
        {
            using var _ = TenantContext();
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "Get", "secret-1"));

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeFalse();
            _session.ConnectedHost.Should().BeNull();
        }

        [Fact]
        public async Task Send_TokenAcquisitionFailure_MakesNoSmtpConnection()
        {
            using var _ = TenantContext();
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("AADSTS7000215"));

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeFalse();
            _session.ConnectedHost.Should().BeNull();
        }

        // ---------- C3 ----------

        [Fact]
        public async Task Send_AuthenticationRejected_ReturnsFalseAndDoesNotSend()
        {
            using var _ = TenantContext();
            _session.ThrowOnAuthenticate = new AuthenticationException("rejected");

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeFalse();
            _session.SendCount.Should().Be(0);
        }

        [Fact]
        public async Task Send_SubmissionFailure_PerformsNoResend()
        {
            using var _ = TenantContext();
            _session.ThrowOnSend = new SmtpProtocolException("broken");

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeFalse();
            _session.SendCount.Should().Be(1, "the one attempt was made; nothing retries it");
        }

        // ---------- classification ----------

        [Fact]
        public void Classify_UsesTypedExceptionsAndStages()
        {
            Office365SmtpClient.Classify(new AuthenticationException("no"))
                .Should().Be(Office365FailureCode.AuthenticationFailed);

            Office365SmtpClient.Classify(new SslHandshakeException("no"))
                .Should().Be(Office365FailureCode.TlsFailed);

            Office365SmtpClient.Classify(new SmtpProtocolException("no"))
                .Should().Be(Office365FailureCode.SmtpFailed);

            Office365SmtpClient.Classify(new InvalidOperationException("no"))
                .Should().Be(Office365FailureCode.SmtpFailed);
        }

        [Fact]
        public void Classify_RecipientRejection_IsNamed()
        {
            var exception = new SmtpCommandException(
                SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "nope");

            Office365SmtpClient.Classify(exception).Should().Be(Office365FailureCode.RecipientRejected);
        }

        [Fact]
        public void Classify_SendAsDenial_NeedsBothTheStageAndTheStatus()
        {
            var denied = new SmtpCommandException(
                SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable, "nope");
            Office365SmtpClient.Classify(denied).Should().Be(Office365FailureCode.SendAsDenied);

            // Ambiguous: the same stage with a different status says nothing about permissions, and
            // a confidently wrong code sends an operator to fix something that was never broken.
            var ambiguous = new SmtpCommandException(
                SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.TransactionFailed, "nope");
            Office365SmtpClient.Classify(ambiguous).Should().Be(Office365FailureCode.SmtpFailed);
        }

        /// <summary>A sender whose socket is a recorder.</summary>
        private sealed class TestableOffice365SmtpClient : Office365SmtpClient
        {
            private readonly RecordingSession _session;

            public TestableOffice365SmtpClient(IOffice365TokenProvider tokens, RecordingSession session)
                : base(tokens, NullLogger<Office365SmtpClient>.Instance)
            {
                _session = session;
            }

            protected override IMailKitSmtpClient CreateSmtpClient() => _session;
        }

        private sealed class RecordingSession : IMailKitSmtpClient
        {
            public string? ConnectedHost { get; private set; }
            public int ConnectedPort { get; private set; }
            public SecureSocketOptions? SocketOptions { get; private set; }
            public SaslMechanism? Mechanism { get; private set; }
            public MimeMessage? Sent { get; private set; }
            public int SendCount { get; private set; }
            public bool Disconnected { get; private set; }
            public bool UsedPasswordConnect { get; private set; }
            public bool UsedPasswordAuthenticate { get; private set; }

            public Exception? ThrowOnAuthenticate { get; set; }
            public Exception? ThrowOnSend { get; set; }
            public Exception? ThrowOnDisconnect { get; set; }

            public Task ConnectAsync(string host, int port, bool useSsl)
            {
                UsedPasswordConnect = true;
                return Task.CompletedTask;
            }

            public Task AuthenticateAsync(string userName, string password)
            {
                UsedPasswordAuthenticate = true;
                return Task.CompletedTask;
            }

            public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions)
            {
                ConnectedHost = host;
                ConnectedPort = port;
                SocketOptions = socketOptions;
                return Task.CompletedTask;
            }

            public Task AuthenticateAsync(SaslMechanism mechanism)
            {
                if (ThrowOnAuthenticate is not null) throw ThrowOnAuthenticate;
                Mechanism = mechanism;
                return Task.CompletedTask;
            }

            public Task SendAsync(MimeMessage message)
            {
                SendCount++;
                Sent = message;
                if (ThrowOnSend is not null) throw ThrowOnSend;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(bool quit)
            {
                if (ThrowOnDisconnect is not null) throw ThrowOnDisconnect;
                Disconnected = true;
                return Task.CompletedTask;
            }

            public void Dispose() { }
        }
    }
}
