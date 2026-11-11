using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Blocks.Genesis;
using FluentAssertions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The bounded retry and the per-attempt diagnostics of <see cref="MailKitSmtpClient"/>
    /// (#332): scripted sessions on a manual clock, so the 10 s and 60 s waits and the 30 s
    /// deadline are asserted exactly without real waiting.
    /// </summary>
    public class MailKitSmtpClientRetryTests
    {
        private const string Password = "s3cret-Passw0rd";
        private const string BodyText = "Your code is 918273";
        private const string Recipient = "recipient@example.com";
        private const string AllTimingsOfAFullAttempt =
            "tcpMs=40 tlsToCertificateMs=150 certificateToReadyMs=60 authMs=200 attemptMs=450 chainStatus=NoError certificateAccepted=true";
        private const string NoPhaseReached =
            "tcpMs=n/a tlsToCertificateMs=n/a certificateToReadyMs=n/a authMs=n/a attemptMs=0 chainStatus=n/a certificateAccepted=n/a";

        private readonly ManualTimeProvider _time = new();
        private readonly LevelLogger<MailKitSmtpClient> _log = new();

        private ScriptedMailKitSmtpClient Client(Action<int, ScriptedSmtpSession>? script = null, IConfiguration? configuration = null) =>
            new(_log, configuration ?? new ConfigurationBuilder().Build(), _time, script);

        private static MailToBeSent AMail(bool enableSsl = true, bool sesHeaders = false) => new()
        {
            ItemId = "mail-1",
            To = [Recipient],
            MailServerConfiguration = new MailServerConfiguration
            {
                Host = "email-smtp.example.com",
                Port = 465,
                EnableSSL = enableSsl,
                SenderUserName = "smtp-user",
                AccountPassword = Password,
                SenderName = "Blocks",
                SenderAddress = "no-reply@example.com",
                IsEnableSnsConfiguration = sesHeaders
            }
        };

        private static MailBody ABody() => new()
        {
            Subject = "Your code",
            Body = $"<p>{BodyText}</p>",
            Attachments = []
        };

        /// <summary>Drives the waits the sender starts, in order, then returns its result.</summary>
        private async Task<bool> Run(Task<bool> send, params int[] waitSeconds)
        {
            foreach (var seconds in waitSeconds)
            {
                await _time.WaitForTimerAsync(TimeSpan.FromSeconds(seconds));
                _time.Advance(TimeSpan.FromSeconds(seconds));
            }

            return await send.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private IReadOnlyList<string> Warnings => _log.At(LogLevel.Warning);
        private IReadOnlyList<string> Errors => _log.At(LogLevel.Error);
        private IReadOnlyList<string> Connecting => _log.Containing("SMTP (MailKit): connecting to");
        private IReadOnlyList<string> Authenticated => _log.Containing("SMTP (MailKit): authenticated as");

        private static IOException DecryptFailure() => new("The decryption operation failed, see inner exception.");

        // ---------------- happy path ----------------

        [Fact]
        public async Task FirstAttemptSucceeds_SendsOnce_WaitsNever_AndLogsEveryTiming()
        {
            // E1, H1, H2, H6, H11.
            var client = Client();

            var result = await Run(client.SendAsync(AMail(), ABody()));

            result.Should().BeTrue();
            var session = client.Sessions.Should().ContainSingle().Subject;
            session.ConnectCount.Should().Be(1);
            session.AuthenticateCount.Should().Be(1);
            session.SendCount.Should().Be(1);
            session.Disconnected.Should().BeTrue();
            session.Disposed.Should().BeTrue();
            _time.Waits.Should().BeEmpty();
            Warnings.Should().BeEmpty();
            Errors.Should().BeEmpty();
            Connecting.Should().ContainSingle().Which.Should().Be(
                "SMTP (MailKit): connecting to email-smtp.example.com:465 ssl=True for itemId=mail-1 with 0 attachment(s) attempt=1/3");
            Authenticated.Should().ContainSingle().Which.Should().Be(
                "SMTP (MailKit): authenticated as smtp-user for itemId=mail-1 attempt=1/3 " + AllTimingsOfAFullAttempt);
        }

        [Fact]
        public async Task TransientConnectFailure_RetriesAfterTenSeconds_OnANewSession()
        {
            // E2, H3, H5, H6.
            var client = Client((n, s) => { if (n == 1) s.FailConnect = DecryptFailure(); });

            var send = client.SendAsync(AMail(), ABody());
            await _time.WaitForTimerAsync(TimeSpan.FromSeconds(10));
            client.Sessions[0].Disposed.Should().BeTrue("the failed session is disposed before the wait");
            var result = await Run(send, 10);

            result.Should().BeTrue();
            client.Sessions.Should().HaveCount(2);
            client.Sessions[0].Should().NotBeSameAs(client.Sessions[1]);
            var failedAt = client.Sessions[0].DisposedAt!.Value;
            (client.Sessions[1].ConnectStartedAt!.Value - failedAt).Should().Be(TimeSpan.FromSeconds(10));
            client.Sessions.Sum(s => s.SendCount).Should().Be(1);
            _time.Waits.Should().Equal(TimeSpan.FromSeconds(10));

            Warnings.Should().ContainSingle().Which.Should().Be(
                "SMTP (MailKit) attempt 1/3 failed at connect for itemId=mail-1 host=email-smtp.example.com:465: " +
                "IOException: The decryption operation failed, see inner exception.; retrying in 10s " +
                "tcpMs=40 tlsToCertificateMs=150 certificateToReadyMs=n/a authMs=n/a attemptMs=190 chainStatus=NoError certificateAccepted=true");
            Connecting.Should().SatisfyRespectively(
                first => first.Should().EndWith(" attempt=1/3"),
                second => second.Should().EndWith(" attempt=2/3"));
            Authenticated.Should().ContainSingle().Which.Should().EndWith("attempt=2/3 " + AllTimingsOfAFullAttempt);
            Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task TwoTransientFailures_WaitTenThenSixtySeconds_AndSendExactlyOnce()
        {
            // H4, H5.
            var client = Client((n, s) => { if (n < 3) s.FailBeforeTcp = new SocketException((int)SocketError.ConnectionReset); });

            var result = await Run(client.SendAsync(AMail(), ABody()), 10, 60);

            result.Should().BeTrue();
            _time.Waits.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));
            client.Sessions.Select(s => s.ConnectStartedAt).Should().Equal(
                TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(70));
            client.Sessions.Sum(s => s.SendCount).Should().Be(1);
            Warnings.Should().HaveCount(2);
            Warnings[0].Should().Contain("attempt 1/3 failed at connect").And.Contain("SocketException").And.Contain("retrying in 10s");
            Warnings[1].Should().Contain("attempt 2/3 failed at connect").And.Contain("retrying in 60s");
        }

        [Fact]
        public async Task SesHeaders_AreAddedOnce_InTheSendersTenant_AfterARetry()
        {
            // H8, D5: the headers go on once, after the login that succeeded, and X-Tenant-Id is
            // read from the send's own context even though the wait ended on another context.
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["SnsConfigurationName"] = "ses-config" })
                .Build();
            var client = Client((n, s) => { if (n == 1) s.FailAuthenticate = new SmtpProtocolException("unexpected disconnect"); }, configuration);

            Task<bool> send;
            using (InTenant("tenant-a"))
            {
                send = client.SendAsync(AMail(sesHeaders: true), ABody());
            }

            bool result;
            using (InTenant("tenant-b"))
            {
                result = await Run(send, 10);
            }

            result.Should().BeTrue();
            var sent = client.Sessions.Single(s => s.Sent is not null).Sent!;
            sent.Headers.Where(h => h.Field == "X-SES-CONFIGURATION-SET").Select(h => h.Value).Should().Equal("ses-config");
            sent.Headers.Where(h => h.Field == "X-Tenant-Id").Select(h => h.Value).Should().Equal("tenant-a");
            sent.Headers.Count(h => h.Field == "X-Mail-Body").Should().Be(1);
        }

        [Theory]
        [InlineData(true, SecureSocketOptions.SslOnConnect)]
        [InlineData(false, SecureSocketOptions.StartTlsWhenAvailable)]
        public async Task EveryAttempt_UsesMailKitsMappingOfEnableSsl(bool enableSsl, SecureSocketOptions expected)
        {
            // H10.
            var client = Client((n, s) => { if (n == 1) s.FailConnect = new TimeoutException(); });

            (await Run(client.SendAsync(AMail(enableSsl), ABody()), 10)).Should().BeTrue();

            client.Sessions.Select(s => s.SocketOptions).Should().Equal(expected, expected);
            client.Sessions.Should().OnlyContain(s => s.Host == "email-smtp.example.com" && s.Port == 465);
        }

        [Fact]
        public async Task ChainWithInformationalStatus_IsAcceptedAndItsFlagsLogged()
        {
            // H11: no policy error means accepted, whatever informational status the chain carries.
            using var chain = SelfSignedChain();
            var client = Client((_, s) => s.Chain = chain);

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeTrue();

            Authenticated.Single().Should().Contain("chainStatus=UntrustedRoot").And.EndWith("certificateAccepted=true");
        }

        [Theory]
        [InlineData(421)]
        [InlineData(450)]
        [InlineData(451)]
        [InlineData(454)]
        public async Task TemporarySmtpReply_IsRetried(int status)
        {
            // H12.
            var client = Client((n, s) => { if (n == 1) s.FailConnect = Reply(status); });

            (await Run(client.SendAsync(AMail(), ABody()), 10)).Should().BeTrue();

            client.Sessions.Should().HaveCount(2);
            Warnings.Should().ContainSingle().Which.Should().Contain("SmtpCommandException");
        }

        [Fact]
        public async Task TestMail_MakesOneAttempt_AndStillLogsItsTimings()
        {
            // Q2 (answer B): the console's test button reports a configuration error at once.
            var mail = AMail();
            mail.IsTestMail = true;
            var client = Client((_, s) => s.FailConnect = DecryptFailure());

            (await Run(client.SendAsync(mail, ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle();
            _time.Waits.Should().BeEmpty();
            Warnings.Should().BeEmpty();
            Connecting.Single().Should().EndWith("attempt=1/1");
            Errors.Single().Should().Contain("attempts=1 stage=connect tcpMs=40 tlsToCertificateMs=150");
        }

        [Fact]
        public async Task SuccessfulTestMail_LogsItsTimings()
        {
            var mail = AMail();
            mail.IsTestMail = true;

            (await Run(Client().SendAsync(mail, ABody()))).Should().BeTrue();

            Authenticated.Single().Should().EndWith("attempt=1/1 " + AllTimingsOfAFullAttempt);
        }

        [Fact]
        public async Task EachAttempt_ReadsThePasswordFromTheRecord()
        {
            // Drift note: the client is a singleton and keeps no copy of the password.
            var mail = AMail();
            var client = Client((n, s) => { if (n == 1) s.FailAuthenticate = new ServiceNotConnectedException(); });

            var send = client.SendAsync(mail, ABody());
            await _time.WaitForTimerAsync(TimeSpan.FromSeconds(10));
            mail.MailServerConfiguration.AccountPassword = "rotated";
            (await Run(send, 10)).Should().BeTrue();

            client.Sessions.Select(s => s.Credentials).Should().Equal(("smtp-user", Password), ("smtp-user", "rotated"));
        }

        // ---------------- critical path ----------------

        [Fact]
        public async Task AllAttemptsFailTransiently_TwoWarningsOneErrorAndNoSend()
        {
            // E3, C4.
            var client = Client((_, s) => s.FailConnect = DecryptFailure());

            var result = await Run(client.SendAsync(AMail(), ABody()), 10, 60);

            result.Should().BeFalse();
            client.Sessions.Should().HaveCount(3).And.OnlyContain(s => s.SendCount == 0 && s.Disposed);
            Warnings.Should().HaveCount(2);
            Errors.Should().ContainSingle().Which.Should().StartWith(
                "SMTP (MailKit) FAILED for itemId=mail-1 host=email-smtp.example.com:465: IOException: The decryption operation failed, see inner exception. attempts=3 stage=connect tcpMs=40");
        }

        [Fact]
        public async Task RejectedCredentials_AreNeverRetried()
        {
            // E4, C1.
            var client = Client((_, s) => s.FailAuthenticate = new AuthenticationException("535 5.7.8 Authentication credentials invalid"));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            var session = client.Sessions.Should().ContainSingle().Subject;
            session.ConnectCount.Should().Be(1);
            session.AuthenticateCount.Should().Be(1);
            _time.Waits.Should().BeEmpty();
            Warnings.Should().BeEmpty();
            Errors.Single().Should().Contain("AuthenticationException").And.Contain(" attempts=1 stage=authenticate ");
        }

        [Theory]
        [InlineData(500)]
        [InlineData(535)]
        [InlineData(550)]
        [InlineData(554)]
        public async Task PermanentSmtpReply_IsNotRetried(int status)
        {
            // C2.
            var client = Client((_, s) => s.FailAuthenticate = Reply(status));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle();
            Warnings.Should().BeEmpty();
            Errors.Single().Should().Contain("stage=authenticate");
        }

        [Fact]
        public async Task FailureDuringData_IsNeverRetried()
        {
            // E5, C3.
            var client = Client((_, s) => s.FailSend = new IOException("connection reset during DATA"));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle().Which.SendCount.Should().Be(1);
            _time.Waits.Should().BeEmpty();
            Warnings.Should().BeEmpty();
            Errors.Single().Should().Contain(" attempts=1 stage=send ").And.Contain("authMs=200");
        }

        [Fact]
        public async Task FailureAtDisconnect_IsNeverRetried()
        {
            // C3 (and the unchanged MailKit result for a failed goodbye, §11).
            var client = Client((_, s) => s.FailDisconnect = new IOException("reset"));

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle().Which.SendCount.Should().Be(1);
            Errors.Single().Should().Contain(" attempts=1 stage=disconnect ");
        }

        [Fact]
        public async Task ConnectStall_IsCancelledAtTheDeadline_AndRetried()
        {
            // E6, C5.
            var client = Client((n, s) => s.StallConnect = n == 1);

            var result = await Run(client.SendAsync(AMail(), ABody()), 10);

            result.Should().BeTrue();
            client.Sessions[0].DisposedAt.Should().Be(TimeSpan.FromSeconds(30));
            client.Sessions[1].ConnectStartedAt.Should().Be(TimeSpan.FromSeconds(40));
            Warnings.Should().ContainSingle().Which.Should()
                .Contain("attempt 1/3 failed at connect")
                .And.Contain("TaskCanceledException")
                .And.Contain("retrying in 10s")
                .And.Contain("tcpMs=n/a")
                .And.Contain("attemptMs=30000");
        }

        [Fact]
        public async Task EveryAttemptStalls_ReportsFailureWithin165Seconds()
        {
            // C5 worst case: 3 x 30 s + 10 s + 60 s.
            var client = Client((_, s) => s.StallConnect = true);

            var result = await Run(client.SendAsync(AMail(), ABody()), 10, 60);

            result.Should().BeFalse();
            _time.Elapsed.Should().Be(TimeSpan.FromSeconds(160)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(165));
            Errors.Single().Should().Contain("attempts=3 stage=connect");
        }

        [Fact]
        public async Task LoginStall_IsCancelledAtTheDeadline_AndRetried()
        {
            // C5: the deadline covers connect and login together.
            var client = Client((n, s) => s.StallAuthenticate = n == 1);

            (await Run(client.SendAsync(AMail(), ABody()), 10)).Should().BeTrue();

            client.Sessions[0].DisposedAt.Should().Be(TimeSpan.FromSeconds(30));
            Warnings.Single().Should()
                .Contain("attempt 1/3 failed at authenticate")
                .And.Contain("TaskCanceledException")
                .And.Contain("authMs=n/a attemptMs=30000");
        }

        [Fact]
        public async Task CancellationTheDeadlineDidNotCause_IsNotRetried()
        {
            // §6: only the attempt deadline makes a cancellation transient.
            var client = Client((_, s) => s.FailAuthenticate = new OperationCanceledException());

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle();
            Errors.Single().Should().Contain("OperationCanceledException").And.Contain("stage=authenticate");
        }

        [Fact]
        public async Task RejectedCertificate_IsNotRetried_AndLogsTheChainStatus()
        {
            // E8, C6.
            using var chain = SelfSignedChain();
            var client = Client((_, s) =>
            {
                s.Chain = chain;
                s.CertificateErrors = SslPolicyErrors.RemoteCertificateChainErrors;
            });

            (await Run(client.SendAsync(AMail(), ABody()))).Should().BeFalse();

            client.Sessions.Should().ContainSingle();
            Warnings.Should().BeEmpty();
            Errors.Single().Should()
                .Contain("SslHandshakeException")
                .And.Contain(" attempts=1 stage=connect ")
                .And.Contain("chainStatus=UntrustedRoot")
                .And.EndWith("certificateAccepted=false");
        }

        [Fact]
        public async Task HandshakeFailureAfterAnAcceptedCertificate_IsRetried()
        {
            // §6: SslHandshakeException is transient when the check accepted the certificate.
            var client = Client((n, s) => { if (n == 1) s.FailConnect = new SslHandshakeException("handshake reset"); });

            (await Run(client.SendAsync(AMail(), ABody()), 10)).Should().BeTrue();

            Warnings.Single().Should().Contain("SslHandshakeException").And.EndWith("certificateAccepted=true");
        }

        [Fact]
        public async Task HandshakeFailureBeforeTheCertificateCheck_IsRetried()
        {
            // §6: and when the check was never reached.
            var client = Client((n, s) =>
            {
                if (n != 1) return;
                s.CertificateErrors = null;
                s.FailConnect = new SslHandshakeException("handshake reset");
            });

            (await Run(client.SendAsync(AMail(), ABody()), 10)).Should().BeTrue();

            Warnings.Single().Should().Contain("chainStatus=n/a certificateAccepted=n/a");
        }

        [Fact]
        public async Task Logs_CarryNoPasswordBodyRecipientOrCertificateData()
        {
            // C7: every new and extended line, across retries and the final failure.
            using var chain = SelfSignedChain();
            var client = Client((n, s) =>
            {
                s.Chain = chain;
                if (n < 3) s.FailAuthenticate = new IOException("reset");
                else s.FailSend = new IOException("reset");
            });

            (await Run(client.SendAsync(AMail(), ABody()), 10, 60)).Should().BeFalse();

            var all = _log.All;
            all.Should().NotContain(Password);
            all.Should().NotContain(BodyText);
            all.Should().NotContain(Recipient);
            all.Should().NotContain(chain.ChainElements[0].Certificate.Thumbprint);
            all.Should().NotContain("CN=smtp.test");
            all.Should().NotContain("BEGIN CERTIFICATE");
        }

        [Fact]
        public async Task OtherMails_KeepSendingWhileOneWaitsBetweenAttempts()
        {
            // C11 (unit part): the singleton keeps no per-mail state, so a mail to a healthy
            // server completes on its first attempt while another mail waits.
            var client = Client((_, s) => s.BeforeConnect = x =>
            {
                if (x.Host == "down.example.com") x.FailBeforeTcp = new SocketException((int)SocketError.HostUnreachable);
            });
            var down = AMail();
            down.ItemId = "mail-down";
            down.MailServerConfiguration.Host = "down.example.com";
            var healthy = AMail();
            healthy.ItemId = "mail-ok";

            var waiting = client.SendAsync(down, ABody());
            await _time.WaitForTimerAsync(TimeSpan.FromSeconds(10));

            (await client.SendAsync(healthy, ABody()).WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
            waiting.IsCompleted.Should().BeFalse("the first mail is still waiting for its second attempt");
            _log.Containing("authenticated as smtp-user for itemId=mail-ok attempt=1/3 ").Should().ContainSingle()
                .Which.Should().Contain("attemptMs=450");

            _time.Advance(TimeSpan.FromSeconds(10));
            (await Run(waiting, 60)).Should().BeFalse();
            client.Sessions.Count(s => s.Host == "down.example.com").Should().Be(3);
        }

        [Fact]
        public async Task UnreachedPhases_AreLoggedAsNotAvailable_NeverAsAnEarlierAttemptsValues()
        {
            // C14: attempt 1 reaches the certificate check; attempt 2 fails before TCP and must
            // not show attempt 1's values.
            var client = Client((n, s) =>
            {
                if (n == 1) s.FailConnect = new IOException("reset");
                if (n == 2) s.FailBeforeTcp = new SocketException((int)SocketError.TimedOut);
            });

            (await Run(client.SendAsync(AMail(), ABody()), 10, 60)).Should().BeTrue();

            Warnings[0].Should().EndWith("tcpMs=40 tlsToCertificateMs=150 certificateToReadyMs=n/a authMs=n/a attemptMs=190 chainStatus=NoError certificateAccepted=true");
            Warnings[1].Should().EndWith(NoPhaseReached);
        }

        // ---------------- the classifier, row by row (§6) ----------------

        public static TheoryData<string, Exception, bool?, bool, bool> ClassifierRows() => new()
        {
            { "IOException", new IOException("decrypt failed"), null, false, true },
            { "SocketException", new SocketException((int)SocketError.ConnectionReset), null, false, true },
            { "SslHandshake, certificate accepted", new SslHandshakeException("reset"), true, false, true },
            { "SslHandshake, check never reached", new SslHandshakeException("reset"), null, false, true },
            { "SslHandshake, certificate rejected", new SslHandshakeException("rejected"), false, false, false },
            { "TimeoutException", new TimeoutException(), null, false, true },
            { "deadline cancellation", new OperationCanceledException(), null, true, true },
            { "other cancellation", new OperationCanceledException(), null, false, false },
            { "SmtpProtocolException", new SmtpProtocolException("bad reply"), null, false, true },
            { "ServiceNotConnectedException", new ServiceNotConnectedException(), null, false, true },
            { "421", Reply(421), null, false, true },
            { "499", Reply(499), null, false, true },
            { "500", Reply(500), null, false, false },
            { "535", Reply(535), null, false, false },
            { "AuthenticationException", new AuthenticationException("bad credentials"), null, false, false },
            { "TLS AuthenticationException (other type)", new System.Security.Authentication.AuthenticationException("tls"), null, false, false },
            { "any other type", new InvalidOperationException("other"), null, false, false }
        };

        [Theory]
        [MemberData(nameof(ClassifierRows))]
        public void Classifier_FollowsTheTransientTable(string row, Exception exception, bool? certificateAccepted, bool deadlineExpired, bool transient)
        {
            SmtpConnectRetryPolicy.IsTransient(exception, certificateAccepted, deadlineExpired).Should().Be(transient, row);
        }

        [Fact]
        public void Policy_IsThreeAttemptsWithTenThenSixtySecondWaitsAndAThirtySecondDeadline()
        {
            SmtpConnectRetryPolicy.MaxAttempts.Should().Be(3);
            SmtpConnectRetryPolicy.WaitAfter(1).Should().Be(TimeSpan.FromSeconds(10));
            SmtpConnectRetryPolicy.WaitAfter(2).Should().Be(TimeSpan.FromSeconds(60));
            SmtpConnectRetryPolicy.AttemptDeadline.Should().Be(TimeSpan.FromSeconds(30));
        }

        // ---------------- helpers ----------------

        private static SmtpCommandException Reply(int status) =>
            new(SmtpErrorCode.UnexpectedStatusCode, (SmtpStatusCode)status, "reply");

        internal static X509Chain SelfSignedChain()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=smtp.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(certificate);
            return chain;
        }

        private static IDisposable InTenant(string tenantId)
        {
            var previous = BlocksContext.GetContext();
            BlocksContext.SetContext(
                BlocksContext.Create(tenantId, [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", tenantId),
                true);
            return new RestoreContext(previous);
        }

        private sealed class RestoreContext(BlocksContext? previous) : IDisposable
        {
            public void Dispose() => BlocksContext.SetContext(previous!, previous is not null);
        }
    }
}
