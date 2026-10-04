using Mail.DomainService.Entities;
using MailBoxSyncService.Entities;
using MailKit.Security;
using MimeKit;

namespace MailBoxSyncService.Services
{
    public interface IMailBoxSyncService
    {
        /// <summary>Syncs the inbox, signing in with the record's username and password.</summary>
        Task SyncInboxAsync(MailServerConfiguration config, string tenantId);

        /// <summary>
        /// Syncs the inbox, signing in with a SASL mechanism (XOAUTH2) when a new session has to be
        /// opened. An already authenticated session is reused and the mechanism goes unused.
        /// </summary>
        Task SyncInboxAsync(MailServerConfiguration config, string tenantId, SaslMechanism saslMechanism);

        /// <summary>
        /// Stores one inbound message and publishes its email trigger, whichever transport read it.
        /// </summary>
        /// <returns>
        /// False when the message has no <c>Message-ID</c> or is already stored for the tenant, and
        /// nothing was written.
        /// </returns>
        Task<bool> StoreInboundAsync(MailServerConfiguration config, string tenantId, MimeMessage message);

        Task SyncOutgoingAsync(SesEventNotification? sesEvent, string tenantId);
    }
}
