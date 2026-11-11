using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using FluentAssertions;
using Mail.DomainService.Mails;
using MailKit.Security;
using MimeKit;
using XUnitTest.TestHelpers;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The real MailKit session (#332): its own TCP connect, the hand-over to MailKit, the phase
    /// timings and the certificate check, against a local SMTP server.
    /// </summary>
    public class MailKitSmtpClientAdapterTests
    {
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

        [Fact]
        public async Task ConnectsOverItsOwnSocket_AndRecordsTheReachedPhases()
        {
            using var server = new FakeSmtpServer();
            using var adapter = new MailKitSmtpClientAdapter(TimeProvider.System);
            using var cts = new CancellationTokenSource(Bound);

            await adapter.ConnectAsync(server.Host, server.Port, SecureSocketOptions.StartTlsWhenAvailable, cts.Token);
            await adapter.AuthenticateAsync("user", "pass", cts.Token);
            await adapter.SendAsync(AMessage());
            await adapter.DisconnectAsync(true);

            server.Credentials.Should().Equal(("user", "pass"));
            var fields = adapter.Diagnostics.TimingFields();
            fields[0].Should().NotBe(SmtpConnectionDiagnostics.NotReached, "the TCP connection opened");
            fields[3].Should().NotBe(SmtpConnectionDiagnostics.NotReached, "the login ran");
            fields[4].Should().NotBe(SmtpConnectionDiagnostics.NotReached);

            // The local server offers no STARTTLS, so no certificate was ever checked.
            fields[1].Should().Be(SmtpConnectionDiagnostics.NotReached);
            fields[2].Should().Be(SmtpConnectionDiagnostics.NotReached);
            fields[5].Should().Be(SmtpConnectionDiagnostics.NotReached);
            fields[6].Should().Be(SmtpConnectionDiagnostics.NotReached);
            adapter.Diagnostics.CertificateAccepted.Should().BeNull();
        }

        [Fact]
        public async Task ARefusedConnection_FailsWithASocketException_BeforeTcp()
        {
            var port = ClosedPort();
            using var adapter = new MailKitSmtpClientAdapter(TimeProvider.System);
            using var cts = new CancellationTokenSource(Bound);

            var connect = () => adapter.ConnectAsync("127.0.0.1", port, SecureSocketOptions.StartTlsWhenAvailable, cts.Token);

            await connect.Should().ThrowAsync<SocketException>();
            adapter.Diagnostics.TimingFields()[0].Should().Be(SmtpConnectionDiagnostics.NotReached);
        }

        [Fact]
        public async Task ACancelledAttempt_StopsTheConnect()
        {
            using var adapter = new MailKitSmtpClientAdapter(TimeProvider.System);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var connect = () => adapter.ConnectAsync("localhost", 25, SecureSocketOptions.StartTlsWhenAvailable, cts.Token);

            await connect.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task OpenSocket_ReturnsAConnectedSocket()
        {
            using var server = new FakeSmtpServer();
            using var cts = new CancellationTokenSource(Bound);

            using var socket = await MailKitSmtpClientAdapter.OpenSocketAsync(server.Host, server.Port, cts.Token);

            socket.Connected.Should().BeTrue();
            ((IPEndPoint)socket.RemoteEndPoint!).Port.Should().Be(server.Port);
        }

        [Fact]
        public void CertificateCheck_AcceptsExactlyWhenThereIsNoPolicyError()
        {
            // H11, C6: MailKit's default decision, with the outcome recorded.
            using var accepting = new MailKitSmtpClientAdapter(TimeProvider.System);
            accepting.CertificateValidationCallback!(this, null, null, SslPolicyErrors.None).Should().BeTrue();
            accepting.Diagnostics.CertificateAccepted.Should().BeTrue();
            accepting.Diagnostics.ChainStatus.Should().Be("NoError");

            foreach (var errors in new[]
                     {
                         SslPolicyErrors.RemoteCertificateNameMismatch,
                         SslPolicyErrors.RemoteCertificateNotAvailable,
                         SslPolicyErrors.RemoteCertificateChainErrors
                     })
            {
                using var rejecting = new MailKitSmtpClientAdapter(TimeProvider.System);
                rejecting.CertificateValidationCallback!(this, null, null, errors).Should().BeFalse(errors.ToString());
                rejecting.Diagnostics.CertificateAccepted.Should().BeFalse();
            }
        }

        [Fact]
        public void CertificateCheck_RecordsTheChainStatusFlags()
        {
            using var chain = MailKitSmtpClientRetryTests.SelfSignedChain();
            using var adapter = new MailKitSmtpClientAdapter(TimeProvider.System);

            adapter.CertificateValidationCallback!(this, null, chain, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeFalse();

            adapter.Diagnostics.ChainStatus.Should().Contain("UntrustedRoot");
            adapter.Diagnostics.TimingFields()[5].Should().Be(adapter.Diagnostics.ChainStatus);
            adapter.Diagnostics.TimingFields()[6].Should().Be("false");
        }

        private static MimeMessage AMessage()
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse("no-reply@example.com"));
            message.To.Add(MailboxAddress.Parse("someone@example.com"));
            message.Subject = "Hello";
            message.Body = new TextPart("plain") { Text = "Hello" };
            return message;
        }

        private static int ClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
