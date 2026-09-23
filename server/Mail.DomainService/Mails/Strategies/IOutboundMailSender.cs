using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Strategies
{
    /// <summary>
    /// Sends outbound mail for one provider.
    /// </summary>
    /// <remarks>
    /// The extension point that keeps provider knowledge out of shared orchestration. Selection
    /// happens once, by provider, before any transport is resolved — so the legacy adapter can go
    /// on evaluating <c>SmtpClient</c> internally exactly as it does today, while a provider that
    /// has nothing to do with that field never reaches it.
    /// </remarks>
    public interface IOutboundMailSender
    {
        MailServiceProvider Provider { get; }

        Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody);

        /// <summary>
        /// Whether this sender already logs a classified error of its own when a send fails.
        /// </summary>
        /// <remarks>
        /// Lets the orchestrator avoid reporting one failure twice without asking which provider
        /// it is holding. False for the legacy senders, which have always relied on the
        /// orchestrator's summary and whose logs must not change.
        /// </remarks>
        bool EmitsOwnFailureDiagnostic => false;
    }
}
