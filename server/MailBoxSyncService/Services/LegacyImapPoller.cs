using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;

namespace MailBoxSyncService.Services
{
    /// <summary>
    /// The existing IMAP path, behind the inbound strategy contract.
    /// </summary>
    /// <remarks>
    /// A pass-through: connection handling, message reading, deduplication and the trigger event
    /// all stay where they were. Registering it per provider is what lets the worker refuse an
    /// unsupported provider by asking the registry rather than by testing a provider id.
    /// </remarks>
    public abstract class LegacyImapPoller : IInboundMailPoller
    {
        private readonly IMailBoxSyncService _syncService;

        protected LegacyImapPoller(IMailBoxSyncService syncService)
        {
            _syncService = syncService;
        }

        public abstract MailServiceProvider Provider { get; }

        public InboundMailMode Mode => InboundMailMode.Poll;

        public Task PollAsync(MailServerConfiguration configuration, string tenantId, CancellationToken cancellationToken = default) =>
            _syncService.SyncInboxAsync(configuration, tenantId);
    }

    public sealed class AmazonSesImapPoller : LegacyImapPoller
    {
        public AmazonSesImapPoller(IMailBoxSyncService syncService) : base(syncService)
        {
        }

        public override MailServiceProvider Provider => MailServiceProvider.AmazonSes;
    }

    public sealed class ZohoImapPoller : LegacyImapPoller
    {
        public ZohoImapPoller(IMailBoxSyncService syncService) : base(syncService)
        {
        }

        public override MailServiceProvider Provider => MailServiceProvider.Zoho;
    }

    /// <summary>
    /// Gmail over <c>imap.gmail.com:993</c> with the account address and an App Password — the
    /// same username/password session the other password providers use.
    /// </summary>
    public sealed class GmailImapPoller : LegacyImapPoller
    {
        public GmailImapPoller(IMailBoxSyncService syncService) : base(syncService)
        {
        }

        public override MailServiceProvider Provider => MailServiceProvider.Gmail;
    }
}
