using FluentAssertions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;
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

        // ---------- bounded connect retry (#332) ----------

        private readonly ManualTimeProvider _time = new();
        private readonly LevelLogger<Office365SmtpClient> _log = new();

        private ScriptedOffice365SmtpClient Scripted(Action<int, ScriptedSmtpSession> script) => new(_log, _time, script);

        private async Task<bool> Run(Task<bool> send, params int[] waitSeconds)
        {
            foreach (var seconds in waitSeconds)
            {
                await _time.WaitForTimerAsync(TimeSpan.FromSeconds(seconds));
                _time.Advance(TimeSpan.FromSeconds(seconds));
            }

            return await send.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Retry_ThrottledConnect_RetriesAfterTenSecondsWithStartTls()
        {
            // E7, H7.
            var client = Scripted((n, s) =>
            {
                if (n == 1) s.FailConnect = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, (SmtpStatusCode)421, "busy");
            });

            var result = await Run(client.SendAsync(AMail(), ABody()), 10);

            result.Should().BeTrue();
            client.Sessions.Should().HaveCount(2).And.OnlyContain(s =>
                s.Host == "smtp.office365.com" && s.Port == 587 && s.SocketOptions == SecureSocketOptions.StartTls);
            client.Sessions[0].Disposed.Should().BeTrue();
            (client.Sessions[1].ConnectStartedAt - client.Sessions[0].DisposedAt).Should().Be(TimeSpan.FromSeconds(10));
            client.Sessions.Sum(s => s.SendCount).Should().Be(1);
            _log.At(LogLevel.Warning).Should().ContainSingle().Which.Should().StartWith(
                "SMTP (Office 365) attempt 1/3 failed at connect for itemId=mail-1: SmtpCommandException; failureCode=O365_THROTTLED; retrying in 10s tcpMs=40");
            _log.Containing("SMTP (Office 365): connecting to smtp.office365.com:587 for itemId=mail-1 with 0 attachment(s) attempt=").Should().HaveCount(2);
            _log.At(LogLevel.Error).Should().BeEmpty();
        }

        [Fact]
        public async Task Retry_SuccessfulAttempt_LogsItsTimings()
        {
            // §1 / §10 step 6: every attempt logs its phase timings (D9: an Information line after login).
            var client = Scripted((_, _) => { });

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeTrue();

            _log.Containing("SMTP (Office 365): authenticated for itemId=mail-1").Should().ContainSingle().Which.Should().Be(
                "SMTP (Office 365): authenticated for itemId=mail-1 attempt=1/3 tcpMs=40 tlsToCertificateMs=150 certificateToReadyMs=60 authMs=200 attemptMs=450 chainStatus=NoError certificateAccepted=true");
            _log.All.Should().NotContain("password1");
        }

        [Fact]
        public async Task Retry_AllAttemptsFail_TwoWarningsThenTheUnchangedFailureLine()
        {
            var client = Scripted((_, s) => s.FailBeforeTcp = new IOException("reset"));

            (await Run(client.SendAsync(AMail(), ABody()), 10, 60)).Should().BeFalse();

            client.Sessions.Should().HaveCount(3).And.OnlyContain(s => s.SendCount == 0);
            _time.Waits.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));
            _log.At(LogLevel.Warning).Should().HaveCount(2);
            _log.At(LogLevel.Error).Should().ContainSingle().Which.Should().StartWith(
                "MAIL FAILED (Office 365): FailureCode=O365_SMTP_FAILED ItemId=mail-1 CorrelationId=corr-1 Provider=Office365Smtp Host=smtp.office365.com Port=587 ExceptionType=IOException");
        }

        [Fact]
        public async Task Retry_RejectedPassword_IsNotRetried()
        {
            // C9.
            var client = Scripted((_, s) => s.FailAuthenticate = new AuthenticationException("535"));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle().Which.AuthenticateCount.Should().Be(1);
            _time.Waits.Should().BeEmpty();
            _log.At(LogLevel.Warning).Should().BeEmpty();
            _log.At(LogLevel.Error).Single().Should().StartWith("MAIL FAILED (Office 365): FailureCode=O365_AUTHENTICATION_FAILED ");
        }

        [Fact]
        public async Task Retry_DisconnectFailureAfterAcceptance_StaysASuccessOnOneSession()
        {
            // C10: no retry and no resend.
            var client = Scripted((_, s) => s.FailDisconnect = new IOException("reset"));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeTrue();

            client.Sessions.Should().ContainSingle().Which.SendCount.Should().Be(1);
            _log.Containing("SMTP (Office 365): disconnect failed after the message was accepted for itemId=mail-1").Should().ContainSingle();
        }

        [Fact]
        public async Task Retry_FailureDuringData_IsNotRetried()
        {
            // C3 for Office 365.
            var client = Scripted((_, s) => s.FailSend = new IOException("reset"));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle().Which.SendCount.Should().Be(1);
            _log.At(LogLevel.Warning).Should().BeEmpty();
        }

        [Fact]
        public async Task Retry_451_IsRetriedButKeepsItsSmtpFailedCode()
        {
            // D6: Classify is unchanged, so 451 logs O365_SMTP_FAILED while §6 retries it.
            var client = Scripted((n, s) =>
            {
                if (n == 1) s.FailAuthenticate = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, (SmtpStatusCode)451, "later");
            });

            (await Run(client.SendAsync(AMail(), ABody()), 10)).Should().BeTrue();

            _log.At(LogLevel.Warning).Single().Should().Contain("failed at authenticate").And.Contain("failureCode=O365_SMTP_FAILED");
        }

        [Fact]
        public async Task Retry_TestMail_MakesOneAttempt()
        {
            // Q2 (answer B).
            var mail = AMail();
            mail.IsTestMail = true;
            var client = Scripted((_, s) => s.FailConnect = new IOException("reset"));

            (await Run(client.SendAsync(mail, ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle();
            _log.Containing("attempt=1/1").Should().ContainSingle();
            _log.At(LogLevel.Warning).Should().BeEmpty();
        }

        [Fact]
        public async Task Retry_WarningCarriesNoProviderText()
        {
            // C7: type and code only, like Office365FailureLog.
            var client = Scripted((n, s) =>
            {
                if (n == 1) s.FailConnect = new SmtpProtocolException("provider says: mailbox support@contoso.com is busy");
            });

            (await Run(client.SendAsync(AMail(), ABody()), 10)).Should().BeTrue();

            _log.At(LogLevel.Warning).Single().Should().NotContain("provider says").And.NotContain("support@contoso.com");
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

            public Exception? ThrowOnAuthenticate { get; set; }
            public Exception? ThrowOnSend { get; set; }
            public Exception? ThrowOnDisconnect { get; set; }

            public SmtpConnectionDiagnostics Diagnostics { get; } = new(TimeProvider.System);

            public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
            {
                if (ThrowOnAuthenticate is not null) throw ThrowOnAuthenticate;
                AuthenticatedAs = userName;
                return Task.CompletedTask;
            }

            public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken)
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
