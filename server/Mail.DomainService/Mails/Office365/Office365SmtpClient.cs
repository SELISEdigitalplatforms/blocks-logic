using MailKit.Net.Smtp;
using MailKit.Security;
using Mail.DomainService.Entities;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Sends a password record through Exchange Online SMTP with STARTTLS and the mailbox
    /// username and password.
    /// </summary>
    /// <remarks>
    /// Only the password half of Office 365. An OAuth record goes through Microsoft Graph
    /// (<see cref="Office365GraphMailSender"/>) instead, which needs no SMTP AUTH on the tenant or
    /// the mailbox and no Exchange service principal. Graph has no username/password mode, so this
    /// is the only transport a password record can take.
    /// <para>
    /// Reached only through <see cref="Office365MailSender"/>, which has already validated the
    /// record. It never adds SES headers — those belong to a different provider and the contract
    /// refuses a record that asks for them.
    /// </para>
    /// </remarks>
    public class Office365SmtpClient : ISmtpClient
    {
        private readonly ILogger<Office365SmtpClient> _logger;

        public Office365SmtpClient(ILogger<Office365SmtpClient> logger)
        {
            _logger = logger;
        }

        protected virtual IMailKitSmtpClient CreateSmtpClient() => new MailKitSmtpClientAdapter();

        public virtual async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var configuration = mailToBeSent.MailServerConfiguration;
            var message = MailMessageComposer.Compose(mailToBeSent, mailBody);

            using var client = CreateSmtpClient();
            var accepted = false;

            try
            {
                _logger.LogInformation(
                    "SMTP (Office 365): connecting to {Host}:{Port} for itemId={ItemId} with {AttachmentCount} attachment(s)",
                    Office365ConfigurationContract.SmtpHost,
                    Office365ConfigurationContract.SmtpPort,
                    mailToBeSent.ItemId,
                    mailBody.Attachments.Count);

                await client.ConnectAsync(
                    Office365ConfigurationContract.SmtpHost,
                    Office365ConfigurationContract.SmtpPort,
                    SecureSocketOptions.StartTls).ConfigureAwait(false);

                await client.AuthenticateAsync(configuration.SenderUserName, configuration.AccountPassword).ConfigureAwait(false);

                await client.SendAsync(message).ConfigureAwait(false);

                // Past this point the message is Exchange's problem, not ours. Nothing below may
                // turn the result false, and nothing anywhere may resend it.
                accepted = true;
            }
            catch (Exception ex)
            {
                LogFailure(Classify(ex), mailToBeSent, ex);
                return false;
            }

            try
            {
                await client.DisconnectAsync(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Accepted already. A failed goodbye is a cleanup problem; treating it as a send
                // failure would invite a resend of a message that is already on its way.
                _logger.LogWarning(
                    ex,
                    "SMTP (Office 365): disconnect failed after the message was accepted for itemId={ItemId}. ExceptionType={ExceptionType}",
                    mailToBeSent.ItemId,
                    ex.GetType().Name);
            }

            return accepted;
        }

        /// <summary>
        /// Maps a transport failure onto a code.
        /// </summary>
        /// <remarks>
        /// Stage and typed exception first, status code second, and anything ambiguous falls to
        /// <see cref="Office365FailureCode.SmtpFailed"/>. SMTP replies are not reliable enough to
        /// classify on text, and a confidently wrong code is worse than a vague one — it sends an
        /// operator to fix a permission that was never the problem. Reply text is never read.
        /// </remarks>
        internal static string Classify(Exception exception) => exception switch
        {
            AuthenticationException => Office365FailureCode.AuthenticationFailed,
            SslHandshakeException => Office365FailureCode.TlsFailed,
            System.Security.Authentication.AuthenticationException => Office365FailureCode.TlsFailed,
            SmtpCommandException smtp => ClassifySmtpCommand(smtp),
            SmtpProtocolException => Office365FailureCode.SmtpFailed,
            _ => Office365FailureCode.SmtpFailed
        };

        private static string ClassifySmtpCommand(SmtpCommandException exception) => exception.ErrorCode switch
        {
            SmtpErrorCode.RecipientNotAccepted => Office365FailureCode.RecipientRejected,

            // Exchange refuses a From identity the authenticated mailbox may not send as at the
            // sender stage, with a mailbox-unavailable status. That pairing is specific enough to
            // name; the status alone is not.
            SmtpErrorCode.SenderNotAccepted when exception.StatusCode == SmtpStatusCode.MailboxUnavailable
                => Office365FailureCode.SendAsDenied,

            _ when exception.StatusCode == SmtpStatusCode.InsufficientStorage
                || (int)exception.StatusCode == 421
                || (int)exception.StatusCode == 450
                => Office365FailureCode.Throttled,

            _ => Office365FailureCode.SmtpFailed
        };

        private void LogFailure(string code, MailToBeSent mailToBeSent, Exception exception) =>
            Office365FailureLog.Write(
                _logger,
                code,
                mailToBeSent,
                Office365ConfigurationContract.SmtpHost,
                Office365ConfigurationContract.SmtpPort,
                exception);
    }
}
