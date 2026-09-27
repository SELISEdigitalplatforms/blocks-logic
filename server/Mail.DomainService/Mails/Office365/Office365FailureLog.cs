using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The one classified error per failed Office 365 send, whichever transport it took.
    /// </summary>
    /// <remarks>
    /// Carries the code, the mail id, the correlation id, the provider, the endpoint and the
    /// exception type — and nothing else. No token, no secret, no secret reference, no recipient,
    /// no message body, and no raw provider text: the exception is passed to the logger as an
    /// exception so the sanitization policy applies to it, rather than being interpolated into the
    /// message. One template for both transports, so an operator's search for
    /// <c>MAIL FAILED (Office 365)</c> keeps finding every failure.
    /// </remarks>
    internal static class Office365FailureLog
    {
        public static void Write(
            ILogger logger,
            string code,
            MailToBeSent mailToBeSent,
            string host,
            int port,
            Exception? exception,
            string? reason = null)
        {
            logger.LogError(
                exception,
                "MAIL FAILED (Office 365): FailureCode={FailureCode} ItemId={ItemId} CorrelationId={CorrelationId} Provider={Provider} Host={Host} Port={Port} ExceptionType={ExceptionType} Reason={Reason}",
                code,
                mailToBeSent.ItemId,
                mailToBeSent.CorrelationId ?? "none",
                nameof(MailServiceProvider.Office365Smtp),
                host,
                port,
                exception?.GetType().Name ?? "none",
                reason ?? "none");
        }
    }
}
