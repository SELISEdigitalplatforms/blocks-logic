using Blocks.Secrets;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;
using MailBoxSyncService.Entities;
using Microsoft.Graph.Models.ODataErrors;
using MimeKit;

namespace MailBoxSyncService.Services
{
    /// <summary>
    /// Exchange Online inbound through Microsoft Graph, reading the inbox as a delta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Graph rather than IMAP XOAUTH2 for the same reason outbound is: one permission model. The
    /// Entra app needs the <c>Mail.Read</c> application permission and admin consent — the same
    /// token scope the sender already uses — where IMAP needs <c>IMAP.AccessAsApp</c>, an Exchange
    /// service principal and <c>FullAccess</c> on the mailbox, none of which Entra shows. A tenant
    /// that wants the app confined to this mailbox scopes it with an Exchange application access
    /// policy or RBAC for Applications.
    /// </para>
    /// <para>
    /// Each poll resumes from the configuration's stored cursor, so only mail that arrived or
    /// changed since the last round is listed, and the MIME is downloaded only for messages not
    /// already stored. The first round lists the whole inbox, as the IMAP sync always did.
    /// </para>
    /// <para>
    /// The cursor advances only after every message on a page has been stored. A failure part way
    /// leaves it where it was, and the next tick repeats that page; storage dedupes by
    /// <c>Message-ID</c>, so nothing is stored twice.
    /// </para>
    /// </remarks>
    public sealed class Office365GraphInboxPoller : IInboundMailPoller
    {
        /// <summary>
        /// Upper bound on delta pages read in one poll, so a large first round cannot hold the
        /// worker away from every other configuration. The rest follows on later ticks.
        /// </summary>
        internal const int MaxPagesPerPoll = 20;

        /// <summary>
        /// A stored cursor is only followed to Graph. It is a URL read back from the database,
        /// and following one anywhere else would be a request this service never meant to make.
        /// </summary>
        private const string CursorPrefix = "https://" + Office365GraphMailSender.GraphHost + "/";

        private readonly IMailBoxSyncService _syncService;
        private readonly IMailRepository _repository;
        private readonly IOffice365TokenProvider _tokenProvider;
        private readonly IOffice365GraphInboxSessionFactory _sessionFactory;
        private readonly ILogger<Office365GraphInboxPoller> _logger;

        public Office365GraphInboxPoller(
            IMailBoxSyncService syncService,
            IMailRepository repository,
            IOffice365TokenProvider tokenProvider,
            IOffice365GraphInboxSessionFactory sessionFactory,
            ILogger<Office365GraphInboxPoller> logger)
        {
            _syncService = syncService;
            _repository = repository;
            _tokenProvider = tokenProvider;
            _sessionFactory = sessionFactory;
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
                // Before any vault read or request. The reason names a field, never a value.
                _logger.LogWarning(
                    "Graph (Office 365): skipping inbound configuration {ConfigurationId} for tenant {TenantId}: {Reason}",
                    configuration.ItemId,
                    tenantId,
                    invalid);
                return;
            }

            string accessToken;
            try
            {
                accessToken = await _tokenProvider.GetTokenAsync(
                    new Office365TokenRequest(
                        tenantId,
                        configuration.TenantId!,
                        configuration.ClientId!,
                        configuration.ClientSecretReference!,
                        Office365TokenScopes.Graph),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SecretVaultException ex)
            {
                LogFailure(Office365FailureCode.VaultUnavailable, configuration, tenantId, ex);
                return;
            }
            catch (SecretException ex)
            {
                LogFailure(Office365FailureCode.SecretResolutionFailed, configuration, tenantId, ex);
                return;
            }
            catch (Exception ex)
            {
                LogFailure(Office365FailureCode.TokenAcquisitionFailed, configuration, tenantId, ex);
                return;
            }

            try
            {
                await SyncAsync(_sessionFactory.Create(accessToken), configuration, tenantId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogFailure(
                    Office365GraphMailSender.Classify(ex),
                    configuration,
                    tenantId,
                    ex,
                    Office365GraphMailSender.Describe(ex));
            }
        }

        private async Task SyncAsync(
            IOffice365GraphInboxSession session,
            MailServerConfiguration configuration,
            string tenantId,
            CancellationToken cancellationToken)
        {
            var mailbox = configuration.MailboxAddress!;
            var stored = await _repository.GetSyncCursorAsync(configuration.ItemId, tenantId).ConfigureAwait(false);
            var cursor = ResumeFrom(stored, mailbox);
            var restarted = false;

            for (var pages = 0; pages < MaxPagesPerPoll; pages++)
            {
                Office365InboxPage page;
                try
                {
                    page = await session.GetInboxDeltaPageAsync(mailbox, cursor, cancellationToken).ConfigureAwait(false);
                }
                catch (ODataError ex) when (ex.ResponseStatusCode == 410 && cursor is not null && !restarted)
                {
                    // Graph has discarded the sync state behind the cursor. A fresh round lists the
                    // inbox again; storage dedupes, so it costs requests and never duplicates mail.
                    _logger.LogWarning(
                        "Graph (Office 365): delta cursor for inbound configuration {ConfigurationId} of tenant {TenantId} expired; restarting the round.",
                        configuration.ItemId,
                        tenantId);
                    cursor = null;
                    restarted = true;
                    continue;
                }

                foreach (var message in page.Messages)
                {
                    await ImportAsync(session, mailbox, message, configuration, tenantId, cancellationToken).ConfigureAwait(false);
                }

                var next = page.NextLink ?? page.DeltaLink
                    ?? throw new InvalidOperationException("Graph returned a delta page with neither a next nor a delta link.");

                await _repository.SaveSyncCursorAsync(
                    new MailBoxSyncCursor
                    {
                        ConfigurationId = configuration.ItemId,
                        MailboxAddress = mailbox,
                        Cursor = next,
                        UpdatedAtUtc = DateTime.UtcNow
                    },
                    tenantId).ConfigureAwait(false);

                if (page.DeltaLink is not null)
                {
                    return;
                }

                cursor = next;
            }
        }

        private async Task ImportAsync(
            IOffice365GraphInboxSession session,
            string mailbox,
            Office365InboxMessage message,
            MailServerConfiguration configuration,
            string tenantId,
            CancellationToken cancellationToken)
        {
            // Mail without a Message-ID is skipped here as the IMAP sync skipped it: it has no
            // identity to dedupe on, so every round would store it again.
            var messageId = NormalizeMessageId(message.InternetMessageId);
            if (messageId is null)
            {
                return;
            }

            // Checked before the download, which is the expensive part. A delta round lists
            // changes as well as arrivals — a message marked read comes back — and those are
            // already stored.
            if (await _repository.ExistsAsync(messageId, tenantId).ConfigureAwait(false))
            {
                return;
            }

            MimeMessage mime;
            try
            {
                await using var content = await session.GetMimeAsync(mailbox, message.Id, cancellationToken).ConfigureAwait(false);
                mime = await MimeMessage.LoadAsync(content, cancellationToken).ConfigureAwait(false);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 404)
            {
                // Deleted or moved between the listing and the download. There is nothing left
                // to store, and holding the cursor back for it would stall the mailbox for good.
                return;
            }

            using (mime)
            {
                await _syncService.StoreInboundAsync(configuration, tenantId, mime).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Where to resume, or null to start a fresh round.
        /// </summary>
        /// <remarks>
        /// A cursor issued for another mailbox — the configuration was edited — would resume that
        /// mailbox's round, and one that does not point at Graph is not followed at all.
        /// </remarks>
        internal static string? ResumeFrom(MailBoxSyncCursor? stored, string mailbox) =>
            stored is not null
            && string.Equals(stored.MailboxAddress, mailbox, StringComparison.OrdinalIgnoreCase)
            && stored.Cursor.StartsWith(CursorPrefix, StringComparison.OrdinalIgnoreCase)
                ? stored.Cursor
                : null;

        /// <summary>
        /// Graph's <c>internetMessageId</c> in the form MimeKit stores it: without the angle brackets.
        /// </summary>
        /// <remarks>
        /// Only the pre-download check depends on this matching. Storage checks again against the
        /// parsed MIME, so a mismatch costs a download and never a duplicate.
        /// </remarks>
        internal static string? NormalizeMessageId(string? internetMessageId)
        {
            var value = internetMessageId?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (value.Length > 1 && value[0] == '<' && value[^1] == '>')
            {
                value = value[1..^1].Trim();
            }

            return value.Length == 0 ? null : value;
        }

        /// <summary>
        /// The one classified error per failed poll. The same fields as the send failure line,
        /// with the configuration and tenant in place of a mail id: no token, secret, mailbox or
        /// raw provider text.
        /// </summary>
        private void LogFailure(
            string code,
            MailServerConfiguration configuration,
            string tenantId,
            Exception exception,
            string? reason = null) =>
            _logger.LogError(
                exception,
                "MAIL INBOUND FAILED (Office 365): FailureCode={FailureCode} ConfigurationId={ConfigurationId} TenantId={TenantId} Host={Host} ExceptionType={ExceptionType} Reason={Reason}",
                code,
                configuration.ItemId,
                tenantId,
                Office365GraphMailSender.GraphHost,
                exception.GetType().Name,
                reason ?? "none");
    }
}
