using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;
using MailKit.Security;

namespace MailBoxSyncService.Services
{
    /// <summary>
    /// Exchange Online over <c>outlook.office365.com:993</c> with SASL XOAUTH2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the same client-credentials token path as the Office 365 sender, but for the Exchange
    /// Online scope rather than Graph: IMAP is an Exchange protocol and refuses a Graph token. The
    /// Entra app needs the <c>IMAP.AccessAsApp</c> application permission, and its Exchange service
    /// principal needs <c>FullAccess</c> on the mailbox.
    /// </para>
    /// <para>
    /// A token is requested on every poll but the caching provider returns the held one until it
    /// nears expiry, and the sync service only uses it when it has to open a new session.
    /// </para>
    /// </remarks>
    public sealed class Office365ImapPoller : IInboundMailPoller
    {
        private readonly IMailBoxSyncService _syncService;
        private readonly IOffice365TokenProvider _tokenProvider;
        private readonly ILogger<Office365ImapPoller> _logger;

        public Office365ImapPoller(
            IMailBoxSyncService syncService,
            IOffice365TokenProvider tokenProvider,
            ILogger<Office365ImapPoller> logger)
        {
            _syncService = syncService;
            _tokenProvider = tokenProvider;
            _logger = logger;
        }

        public MailServiceProvider Provider => MailServiceProvider.Office365Smtp;

        public InboundMailMode Mode => InboundMailMode.Poll;

        public async Task PollAsync(MailServerConfiguration configuration, string tenantId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var invalid = Office365ConfigurationContract.ValidateInbound(configuration, tenantId);
            if (invalid is not null)
            {
                // Before any vault read or socket. The reason names a field, never a value.
                _logger.LogWarning(
                    "IMAP (Office 365): skipping configuration {ConfigurationId} for tenant {TenantId}: {Reason}",
                    configuration.ItemId,
                    tenantId,
                    invalid);
                return;
            }

            var accessToken = await _tokenProvider.GetTokenAsync(
                new Office365TokenRequest(
                    tenantId,
                    configuration.TenantId!,
                    configuration.ClientId!,
                    configuration.ClientSecretReference!,
                    Office365TokenScopes.ExchangeOnline),
                cancellationToken).ConfigureAwait(false);

            await _syncService.SyncInboxAsync(
                configuration,
                tenantId,
                new SaslMechanismOAuth2(configuration.MailboxAddress, accessToken)).ConfigureAwait(false);
        }
    }
}
