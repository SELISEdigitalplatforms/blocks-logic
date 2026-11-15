using System.Net.Sockets;
using Mail.DomainService.Entities;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// The bounded retry for the SMTP connect and login step, shared by the MailKit and the
    /// Office 365 SMTP senders.
    /// </summary>
    /// <remarks>
    /// Only connect and login are ever retried: once the message has been handed to SMTP DATA
    /// a resend could deliver it twice. Each attempt runs on a new client under a 30 s deadline;
    /// a transient failure waits 10 s before attempt 2 and 60 s before attempt 3, so a mail that
    /// fails every time reports within 3 × 30 s + 10 s + 60 s = 160 s. A test mail gets one
    /// attempt, so the console's test button reports a configuration error at once. The values
    /// are constants on purpose; they are not configurable.
    /// </remarks>
    internal static class SmtpConnectRetryPolicy
    {
        public const int MaxAttempts = 3;

        public const string ConnectStage = "connect";
        public const string AuthenticateStage = "authenticate";
        public const string SendStage = "send";
        public const string DisconnectStage = "disconnect";

        /// <summary>The longest one attempt's connect and login may take.</summary>
        public static readonly TimeSpan AttemptDeadline = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan[] Waits = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)];

        /// <summary>Three attempts, or one for a test mail.</summary>
        public static int MaxAttemptsFor(MailToBeSent mailToBeSent) => mailToBeSent.IsTestMail ? 1 : MaxAttempts;

        /// <summary>The wait before the attempt that follows <paramref name="failedAttempt"/> (1-based).</summary>
        public static TimeSpan WaitAfter(int failedAttempt) => Waits[failedAttempt - 1];

        /// <summary>
        /// Whether a connect or login failure may be retried.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <param name="certificateAccepted">
        /// The certificate check's decision for this attempt, or null when it was never reached. A
        /// handshake failure after a rejected certificate is permanent.
        /// </param>
        /// <param name="deadlineExpired">Whether this attempt's deadline cancelled it.</param>
        public static bool IsTransient(Exception exception, bool? certificateAccepted, bool deadlineExpired) => exception switch
        {
            // Only the attempt deadline makes a cancellation transient; nothing else cancels a send.
            OperationCanceledException => deadlineExpired,

            // Bad credentials: retrying cannot help and could lock the mailbox.
            AuthenticationException => false,
            SmtpCommandException command => (int)command.StatusCode is >= 400 and < 500,
            SslHandshakeException => certificateAccepted != false,
            IOException or SocketException or TimeoutException or SmtpProtocolException or ServiceNotConnectedException => true,
            _ => false
        };

        /// <summary>
        /// Connects and logs in once, under the attempt deadline.
        /// </summary>
        /// <remarks>
        /// The credentials are read from <paramref name="configuration"/> at login, on every
        /// attempt, and nothing here keeps a copy: the senders are singletons, and the password
        /// sits on the record only while the send runs.
        /// </remarks>
        /// <returns>Null when the client is connected and logged in, otherwise the failure.</returns>
        public static async Task<SmtpConnectFailure?> ConnectAndAuthenticateAsync(
            IMailKitSmtpClient client,
            SmtpEndpoint endpoint,
            MailServerConfiguration configuration,
            TimeProvider timeProvider)
        {
            using var deadline = new CancellationTokenSource(AttemptDeadline, timeProvider);
            var stage = ConnectStage;

            try
            {
                await client.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.SocketOptions, deadline.Token).ConfigureAwait(false);

                stage = AuthenticateStage;
                await client.AuthenticateAsync(configuration.SenderUserName, configuration.AccountPassword, deadline.Token).ConfigureAwait(false);

                return null;
            }
            catch (Exception exception)
            {
                var transient = IsTransient(exception, client.Diagnostics.CertificateAccepted, deadline.IsCancellationRequested);
                return new SmtpConnectFailure(exception, stage, transient);
            }
        }
    }

    /// <summary>Where and how an SMTP attempt connects.</summary>
    internal sealed record SmtpEndpoint(string Host, int Port, SecureSocketOptions SocketOptions);

    /// <summary>A failed connect or login: the exception, the stage it failed at, and whether it may be retried.</summary>
    internal sealed record SmtpConnectFailure(Exception Exception, string Stage, bool IsTransient);
}
