using FluentAssertions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The Office 365 password transport: SMTP with STARTTLS and the mailbox credentials. OAuth
    /// records go through Graph and are covered by <see cref="Office365GraphMailSenderTests"/>.
    /// </summary>
    public class Office365SmtpClientTests
    {
        private readonly RecordingSession _session = new();

        private TestableOffice365SmtpClient Client() => new(_session);

        private static MailServerConfiguration Config() => new()
        {
            ItemId = "cfg-1",
            Provider = MailServiceProvider.Office365Smtp,
            IsInbound = false,
            AuthenticationType = MailAuthenticationType.Password,
            SecurityMode = MailSecurityMode.StartTls,
            Host = "smtp.office365.com",
            Port = 587,
            SenderUserName = "support@contoso.com",
            AccountPassword = "password1",
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

        [Fact]
        public async Task Send_UsesStartTlsAndTheMailboxPassword_AndSendsOnce()
        {
            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeTrue();
            _session.ConnectedHost.Should().Be("smtp.office365.com");
            _session.ConnectedPort.Should().Be(587);
            _session.SocketOptions.Should().Be(SecureSocketOptions.StartTls);
            _session.AuthenticatedAs.Should().Be("support@contoso.com");
            _session.Mechanism.Should().BeNull("a password record never presents a token");
            _session.SendCount.Should().Be(1);
            _session.Disconnected.Should().BeTrue();

            // The bool-SSL connect belongs to the legacy providers and must never be reached.
            _session.UsedPasswordConnect.Should().BeFalse();
        }

        [Fact]
        public async Task Send_NeverAddsSesHeaders()
        {
            await Client().SendAsync(AMail(), ABody());

            var headers = _session.Sent!.Headers.Select(h => h.Field).ToList();
            headers.Should().NotContain("X-SES-CONFIGURATION-SET");
            headers.Should().NotContain("X-Tenant-Id");
            headers.Should().NotContain("X-Mail-Body");
        }

        [Fact]
        public async Task Send_ComposesTheSameMessageTheLegacySenderWould()
        {
            await Client().SendAsync(AMail(), ABody());

            _session.Sent!.Subject.Should().Be("Invoice");
            _session.Sent.From.Mailboxes.Single().Address.Should().Be("notifications@contoso.com");
            _session.Sent.From.Mailboxes.Single().Name.Should().Be("Contoso Notifications");
            _session.Sent.To.Mailboxes.Single().Address.Should().Be("recipient@example.com");
        }

        [Fact]
        public async Task Send_DisconnectFailureAfterAcceptance_KeepsTheSuccessAndDoesNotResend()
        {
            _session.ThrowOnDisconnect = new IOException("connection reset");

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeTrue("the message was already accepted; a failed goodbye must not invite a resend");
            _session.SendCount.Should().Be(1);
        }

        [Fact]
        public async Task Send_AuthenticationRejected_ReturnsFalseAndDoesNotSend()
        {
            _session.ThrowOnAuthenticate = new AuthenticationException("rejected");

            var result = await Client().SendAsync(AMail(), ABody());

            result.Should().BeFalse();
            _session.SendCount.Should().Be(0);
        }

        [Fact]
        public async Task Send_SubmissionFailure_PerformsNoResend()
        {
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

            public TestableOffice365SmtpClient(RecordingSession session)
                : base(NullLogger<Office365SmtpClient>.Instance)
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
            public string? AuthenticatedAs { get; private set; }
            public MimeMessage? Sent { get; private set; }
            public int SendCount { get; private set; }
            public bool Disconnected { get; private set; }
            public bool UsedPasswordConnect { get; private set; }

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
                if (ThrowOnAuthenticate is not null) throw ThrowOnAuthenticate;
                AuthenticatedAs = userName;
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
