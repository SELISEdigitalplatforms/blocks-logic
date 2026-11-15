using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Phase timings and the certificate-check outcome of one SMTP connection attempt.
    /// </summary>
    /// <remarks>
    /// One instance per attempt, owned by the session that makes the connection, so a value can
    /// never leak from an earlier attempt into a later one. A phase that was not reached is
    /// reported as <see cref="NotReached"/>, never as 0. Only numbers, chain-status flag names and
    /// a boolean are kept: no certificate content, credential or message data.
    /// </remarks>
    public sealed class SmtpConnectionDiagnostics
    {
        /// <summary>The value logged for a phase the attempt never reached.</summary>
        public const string NotReached = "n/a";

        /// <summary>
        /// The structured-log fields appended to every attempt line, in the order
        /// <see cref="TimingFields"/> returns their values.
        /// </summary>
        /// <remarks>
        /// <c>attemptMs</c> runs from the attempt's start to the moment the line is logged, so on a
        /// failure after login it includes the send; compare connect and login through the other fields.
        /// </remarks>
        public const string TimingTemplate =
            "tcpMs={TcpMs} tlsToCertificateMs={TlsToCertificateMs} certificateToReadyMs={CertificateToReadyMs} authMs={AuthMs} attemptMs={AttemptMs} chainStatus={ChainStatus} certificateAccepted={CertificateAccepted}";

        private const string NoChainError = nameof(X509ChainStatusFlags.NoError);

        private readonly TimeProvider _timeProvider;
        private readonly long _started;
        private long? _tcpConnected;
        private long? _certificateChecked;
        private long? _connected;
        private long? _authenticationStarted;
        private long? _authenticated;

        public SmtpConnectionDiagnostics(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
            _started = timeProvider.GetTimestamp();
        }

        /// <summary>
        /// Whether the certificate check accepted the server certificate, or null when the check
        /// was never reached.
        /// </summary>
        public bool? CertificateAccepted { get; private set; }

        /// <summary>
        /// The chain-status flag names seen by the certificate check, comma-separated, or
        /// <c>NoError</c>; null when the check was never reached.
        /// </summary>
        public string? ChainStatus { get; private set; }

        /// <summary>The TCP connection to the server is open (DNS included).</summary>
        public void MarkTcpConnected() => _tcpConnected = _timeProvider.GetTimestamp();

        /// <summary>The connect step finished: handshake, greeting and EHLO are done.</summary>
        public void MarkConnected() => _connected = _timeProvider.GetTimestamp();

        /// <summary>The login starts.</summary>
        public void MarkAuthenticationStarted() => _authenticationStarted = _timeProvider.GetTimestamp();

        /// <summary>The login succeeded.</summary>
        public void MarkAuthenticated() => _authenticated = _timeProvider.GetTimestamp();

        /// <summary>
        /// Records the certificate check and makes its decision.
        /// </summary>
        /// <returns>
        /// True only when <paramref name="sslPolicyErrors"/> is <see cref="SslPolicyErrors.None"/>:
        /// the same decision as MailKit's default validation, so certificate acceptance is unchanged.
        /// </returns>
        public bool RecordCertificateCheck(X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            _certificateChecked = _timeProvider.GetTimestamp();
            ChainStatus = DescribeChain(chain);
            CertificateAccepted = sslPolicyErrors == SslPolicyErrors.None;
            return CertificateAccepted.Value;
        }

        /// <summary>The values for <see cref="TimingTemplate"/>, measured up to now.</summary>
        public object[] TimingFields()
        {
            var now = _timeProvider.GetTimestamp();
            return
            [
                Milliseconds(_started, _tcpConnected),
                Milliseconds(_tcpConnected, _certificateChecked),
                Milliseconds(_certificateChecked, _connected),
                Milliseconds(_authenticationStarted, _authenticated),
                Milliseconds(_started, now),
                ChainStatus ?? NotReached,
                CertificateAccepted switch
                {
                    true => "true",
                    false => "false",
                    null => NotReached
                }
            ];
        }

        private string Milliseconds(long? from, long? to) =>
            from is { } start && to is { } end
                ? ((long)_timeProvider.GetElapsedTime(start, end).TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : NotReached;

        private static string DescribeChain(X509Chain? chain)
        {
            var flags = X509ChainStatusFlags.NoError;
            foreach (var status in chain?.ChainStatus ?? [])
            {
                flags |= status.Status;
            }

            if (flags == X509ChainStatusFlags.NoError)
            {
                return NoChainError;
            }

            return string.Join(
                ",",
                Enum.GetValues<X509ChainStatusFlags>()
                    .Where(flag => flag != X509ChainStatusFlags.NoError && flags.HasFlag(flag))
                    .Select(flag => flag.ToString()));
        }
    }
}
