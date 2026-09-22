using Blocks.Genesis;
using Blocks.Secrets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Sends through Exchange Online with STARTTLS and SASL XOAUTH2.
    /// </summary>
    /// <remarks>
    /// Never enters the legacy <c>SmtpClient</c> branch, whatever the record stores in that field:
    /// the registry selects by provider first, so the transport question is already answered by
    /// the time this runs. It never adds SES headers either — those belong to a different provider
    /// and the contract refuses a record that asks for them.
    /// </remarks>
    public class Office365SmtpClient : IOutboundMailSender, ISmtpClient
    {
        private readonly IOffice365TokenProvider _tokenProvider;
        private readonly ILogger<Office365SmtpClient> _logger;

        public Office365SmtpClient(IOffice365TokenProvider tokenProvider, ILogger<Office365SmtpClient> logger)
        {
            _tokenProvider = tokenProvider;
            _logger = logger;
        }

        public MailServiceProvider Provider => MailServiceProvider.Office365Smtp;

        /// <summary>
        /// True: every failure path here emits exactly one classified Office 365 error, so the
        /// orchestrator's generic summary would be a second, less useful account of the same send.
        /// </summary>
        public bool EmitsOwnFailureDiagnostic => true;

        protected virtual IMailKitSmtpClient CreateSmtpClient() => new MailKitSmtpClientAdapter();

        public async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var configuration = mailToBeSent.MailServerConfiguration;
            var blocksTenantId = BlocksContext.GetContext()?.TenantId;

            var invalid = Office365ConfigurationContract.Validate(configuration, blocksTenantId);
            if (invalid is not null)
            {
                // Before any secret or network access, which is the point: a bad record costs one
                // log line, not a vault read and a socket.
                LogFailure(Office365FailureCode.ConfigInvalid, mailToBeSent, exception: null, reason: invalid);
                return false;
            }

            string accessToken;
            try
            {
                accessToken = await _tokenProvider.GetTokenAsync(
                    new Office365TokenRequest(
                        blocksTenantId!,
                        configuration.TenantId!,
                        configuration.ClientId!,
                        configuration.ClientSecretReference!))
                    .ConfigureAwait(false);
            }
            catch (SecretVaultException ex)
            {
                LogFailure(Office365FailureCode.VaultUnavailable, mailToBeSent, ex);
                return false;
            }
            catch (SecretException ex)
            {
                LogFailure(Office365FailureCode.SecretResolutionFailed, mailToBeSent, ex);
                return false;
            }
            catch (Exception ex)
            {
                LogFailure(Office365FailureCode.TokenAcquisitionFailed, mailToBeSent, ex);
                return false;
            }

            return await SendWithTokenAsync(mailToBeSent, mailBody, configuration, accessToken).ConfigureAwait(false);
        }

        private async Task<bool> SendWithTokenAsync(
            MailToBeSent mailToBeSent,
            MailBody mailBody,
            MailServerConfiguration configuration,
            string accessToken)
        {
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

                await client.AuthenticateAsync(
                    new SaslMechanismOAuth2(configuration.MailboxAddress, accessToken)).ConfigureAwait(false);

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

        /// <summary>
        /// The one classified error per failed send.
        /// </summary>
        /// <remarks>
        /// Carries the code, the mail id, the correlation id, the provider, the fixed endpoint and
        /// the exception type — and nothing else. No token, no secret, no secret reference, no
        /// recipient, no message body, and no raw provider text: the exception is passed to the
        /// logger as an exception so the sanitization policy applies to it, rather than being
        /// interpolated into the message.
        /// </remarks>
        private void LogFailure(string code, MailToBeSent mailToBeSent, Exception? exception, string? reason = null)
        {
            _logger.LogError(
                exception,
                "MAIL FAILED (Office 365): FailureCode={FailureCode} ItemId={ItemId} CorrelationId={CorrelationId} Provider={Provider} Host={Host} Port={Port} ExceptionType={ExceptionType} Reason={Reason}",
                code,
                mailToBeSent.ItemId,
                mailToBeSent.CorrelationId ?? "none",
                nameof(MailServiceProvider.Office365Smtp),
                Office365ConfigurationContract.SmtpHost,
                Office365ConfigurationContract.SmtpPort,
                exception?.GetType().Name ?? "none",
                reason ?? "none");
        }
    }
}
