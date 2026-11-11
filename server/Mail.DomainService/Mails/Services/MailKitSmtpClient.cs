using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MailKit.Security;
using MimeKit;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// The seam over MailKit's SMTP client, so a sender can be tested without a socket.
    /// </summary>
    /// <remarks>
    /// Connect and password login take a cancellation token, so an attempt can be held to its
    /// deadline, and <see cref="Diagnostics"/> exposes the attempt's phase timings. The SASL login
    /// exists because a username/password pair cannot carry a mechanism such as XOAUTH2.
    /// </remarks>
    public interface IMailKitSmtpClient : IDisposable
    {
        /// <summary>Timings and certificate-check outcome of this client's one connection.</summary>
        SmtpConnectionDiagnostics Diagnostics { get; }

        Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken);
        Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

        /// <summary>Authenticates with a SASL mechanism, for XOAUTH2.</summary>
        Task AuthenticateAsync(SaslMechanism mechanism);

        Task SendAsync(MimeMessage message);
        Task DisconnectAsync(bool quit);
    }

    /// <summary>
    /// The real session. It opens the TCP connection itself, so the TCP time can be measured,
    /// and hands the connected socket to MailKit with the requested socket options.
    /// </summary>
    /// <remarks>
    /// Nothing else on the MailKit client changes: its timeout, revocation check and other
    /// settings keep their defaults, and the certificate callback only records what it saw before
    /// making exactly MailKit's default decision (accept only when there is no policy error).
    /// </remarks>
    internal sealed class MailKitSmtpClientAdapter : IMailKitSmtpClient
    {
        private readonly MailKit.Net.Smtp.SmtpClient _client = new();

        public MailKitSmtpClientAdapter(TimeProvider timeProvider)
        {
            Diagnostics = new SmtpConnectionDiagnostics(timeProvider);
            _client.ServerCertificateValidationCallback = (_, _, chain, sslPolicyErrors) =>
                Diagnostics.RecordCertificateCheck(chain, sslPolicyErrors);
        }

        public SmtpConnectionDiagnostics Diagnostics { get; }

        public async Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken)
        {
            var socket = await OpenSocketAsync(host, port, cancellationToken).ConfigureAwait(false);
            Diagnostics.MarkTcpConnected();

            try
            {
                await _client.ConnectAsync(socket, host, port, socketOptions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            Diagnostics.MarkConnected();
        }

        public async Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            Diagnostics.MarkAuthenticationStarted();
            await _client.AuthenticateAsync(userName, password, cancellationToken).ConfigureAwait(false);
            Diagnostics.MarkAuthenticated();
        }

        public Task AuthenticateAsync(SaslMechanism mechanism) => _client.AuthenticateAsync(mechanism);
        public Task SendAsync(MimeMessage message) => _client.SendAsync(message);
        public Task DisconnectAsync(bool quit) => _client.DisconnectAsync(quit);
        public void Dispose() => _client.Dispose();

        /// <summary>The certificate check MailKit runs during the TLS handshake.</summary>
        internal RemoteCertificateValidationCallback? CertificateValidationCallback => _client.ServerCertificateValidationCallback;

        /// <summary>
        /// Resolves the host and tries each address in turn, as MailKit does, so one unreachable
        /// address cannot fail a host that has a reachable one. IPv6 addresses are skipped on a
        /// host without IPv6, and the lookup itself is held to the attempt deadline.
        /// </summary>
        internal static async Task<Socket> OpenSocketAsync(string host, int port, CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            SocketException? lastFailure = null;

            foreach (var address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
                {
                    continue;
                }

                Socket? socket = null;
                try
                {
                    socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
                    return socket;
                }
                catch (SocketException ex)
                {
                    socket?.Dispose();
                    lastFailure = ex;
                }
                catch
                {
                    socket?.Dispose();
                    throw;
                }
            }

            throw lastFailure ?? new SocketException((int)SocketError.HostNotFound);
        }
    }

    public class MailKitSmtpClient : ISmtpClient
    {
        private const string ConnectingTemplate =
            "SMTP (MailKit): connecting to {Host}:{Port} ssl={EnableSsl} for itemId={ItemId} with {AttachmentCount} attachment(s) attempt={Attempt}/{MaxAttempts}";

        private const string AuthenticatedTemplate =
            "SMTP (MailKit): authenticated as {UserName} for itemId={ItemId} attempt={Attempt}/{MaxAttempts} " + SmtpConnectionDiagnostics.TimingTemplate;

        private const string RetryTemplate =
            "SMTP (MailKit) attempt {Attempt}/{MaxAttempts} failed at {Stage} for itemId={ItemId} host={Host}:{Port}: {ExceptionType}: {Message}; retrying in {DelaySeconds}s " + SmtpConnectionDiagnostics.TimingTemplate;

        private const string FailedTemplate =
            "SMTP (MailKit) FAILED for itemId={ItemId} host={Host}:{Port}: {ExceptionType}: {Message} attempts={Attempts} stage={Stage} " + SmtpConnectionDiagnostics.TimingTemplate;

        private readonly ILogger<MailKitSmtpClient> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeProvider _timeProvider;

        public MailKitSmtpClient(ILogger<MailKitSmtpClient> logger,
                                IConfiguration configuration)
            : this(logger, configuration, TimeProvider.System)
        {
        }

        public MailKitSmtpClient(ILogger<MailKitSmtpClient> logger,
                                IConfiguration configuration,
                                TimeProvider timeProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _timeProvider = timeProvider;
        }

        /// <summary>A new session for each attempt; a failed one is disposed before the wait.</summary>
        protected virtual IMailKitSmtpClient CreateSmtpClient()
        {
            return new MailKitSmtpClientAdapter(_timeProvider);
        }

        public async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            // Composed once per mail; only the connection is retried.
            var message = MailMessageComposer.Compose(mailToBeSent, mailBody);
            var configuration = mailToBeSent.MailServerConfiguration;
            var endpoint = new SmtpEndpoint(
                configuration.Host,
                configuration.Port,
                configuration.EnableSSL ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            var maxAttempts = SmtpConnectRetryPolicy.MaxAttemptsFor(mailToBeSent);

            for (var attempt = 1; ; attempt++)
            {
                TimeSpan wait;
                using (var client = CreateSmtpClient())
                {
                    _logger.LogInformation(
                        ConnectingTemplate,
                        configuration.Host,
                        configuration.Port,
                        configuration.EnableSSL,
                        mailToBeSent.ItemId,
                        mailBody.Attachments.Count,
                        attempt,
                        maxAttempts);

                    var failure = await SmtpConnectRetryPolicy
                        .ConnectAndAuthenticateAsync(client, endpoint, configuration, _timeProvider)
                        .ConfigureAwait(false);

                    if (failure is null)
                    {
                        return await SendConnectedAsync(client, message, mailToBeSent, mailBody, attempt, maxAttempts).ConfigureAwait(false);
                    }

                    if (!failure.IsTransient || attempt >= maxAttempts)
                    {
                        LogFailed(failure.Exception, mailToBeSent, attempt, failure.Stage, client.Diagnostics);
                        return false;
                    }

                    wait = SmtpConnectRetryPolicy.WaitAfter(attempt);
                    _logger.LogWarning(
                        failure.Exception,
                        RetryTemplate,
                        [
                            attempt,
                            maxAttempts,
                            failure.Stage,
                            mailToBeSent.ItemId,
                            configuration.Host,
                            configuration.Port,
                            failure.Exception.GetType().Name,
                            failure.Exception.Message,
                            (int)wait.TotalSeconds,
                            .. client.Diagnostics.TimingFields()
                        ]);
                }

                await Task.Delay(wait, _timeProvider).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sends on a logged-in session. Nothing here is retried: once DATA has started a resend
        /// could deliver the message twice.
        /// </summary>
        private async Task<bool> SendConnectedAsync(
            IMailKitSmtpClient client,
            MimeMessage message,
            MailToBeSent mailToBeSent,
            MailBody mailBody,
            int attempt,
            int maxAttempts)
        {
            var configuration = mailToBeSent.MailServerConfiguration;
            _logger.LogInformation(
                AuthenticatedTemplate,
                [configuration.SenderUserName, mailToBeSent.ItemId, attempt, maxAttempts, .. client.Diagnostics.TimingFields()]);

            var stage = SmtpConnectRetryPolicy.SendStage;
            try
            {
                _logger.LogInformation("Sns configuration enabled: {IsEnableSnsConfiguration}", configuration.SendsSnsHeaders());
                if (configuration.SendsSnsHeaders())
                {
                    // Once per mail, after the login that succeeded, in the send's own tenant context.
                    message.Headers.Add("X-SES-CONFIGURATION-SET", _configuration["SnsConfigurationName"]);
                    message.Headers.Add("X-Tenant-Id", BlocksContext.GetContext()?.TenantId);
                    message.Headers.Add("X-Mail-Body", mailBody.Body);
                }

                await client.SendAsync(message).ConfigureAwait(false);

                stage = SmtpConnectRetryPolicy.DisconnectStage;
                await client.DisconnectAsync(true).ConfigureAwait(false);

                return true;
            }
            catch (Exception e)
            {
                LogFailed(e, mailToBeSent, attempt, stage, client.Diagnostics);
                return false;
            }
        }

        private void LogFailed(Exception e, MailToBeSent mailToBeSent, int attempts, string stage, SmtpConnectionDiagnostics diagnostics)
        {
            // The exception type is the useful bit here: authentication, TLS, size limit and
            // "recipient rejected" all surface as different MailKit exceptions.
            _logger.LogError(
                e,
                FailedTemplate,
                [
                    mailToBeSent.ItemId,
                    mailToBeSent.MailServerConfiguration.Host,
                    mailToBeSent.MailServerConfiguration.Port,
                    e.GetType().Name,
                    e.Message,
                    attempts,
                    stage,
                    .. diagnostics.TimingFields()
                ]);
        }
    }
}
