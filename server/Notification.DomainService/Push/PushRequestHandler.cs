using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>Sends the mobile push of one <see cref="PushRequested"/> (queue <see cref="NotificationQueues.Push"/>).</summary>
    public interface IPushRequestHandler
    {
        Task HandleAsync(PushRequested message, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushRequestHandler"/>
    /// <remarks>
    /// Order: shape → age (<see cref="MaxAge"/>) → dedup by id → tenant's push settings (off or not set up: drop) →
    /// template, texts and options → devices (tenant DB; ROOT DB for root users; seen in the last 60 days; platforms
    /// the notification goes to) → settings again (push or a platform may have been switched off meanwhile) → key and
    /// access token → FCM. A failure before the FCM step releases the dedup claim and is thrown, so a redelivery can
    /// try again. Every drop is logged with the configuration name and a reason, never ids, tokens or payload text.
    /// MaxAge equals the dedup window on purpose: a message older than the window could be a redelivery whose claim
    /// already expired, and a push hours late is noise rather than news.
    /// </remarks>
    public sealed class PushRequestHandler : IPushRequestHandler
    {
        public static readonly TimeSpan MaxAge = PushDedupStore.Window;

        private readonly IPushDedupStore _dedup;
        private readonly IPushSettingsStore _settings;
        private readonly IPushMessageComposer _composer;
        private readonly IPushDeviceStore _devices;
        private readonly IFcmPushSender _sender;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PushRequestHandler> _logger;

        public PushRequestHandler(IPushDedupStore dedup, IPushSettingsStore settings, IPushMessageComposer composer,
            IPushDeviceStore devices, IFcmPushSender sender, TimeProvider timeProvider, ILogger<PushRequestHandler> logger)
        {
            _dedup = dedup;
            _settings = settings;
            _composer = composer;
            _devices = devices;
            _sender = sender;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task HandleAsync(PushRequested message, CancellationToken cancellationToken)
        {
            if (Refusal(message) is { } refusal)
            {
                _logger.LogWarning("Push {ConfigName} dropped: {Reason}", message?.ConfigName, refusal);
                return;
            }

            if (!await _dedup.TryClaimAsync(message.Id, cancellationToken))
            {
                _logger.LogInformation("Push {ConfigName} skipped: already handled", message.ConfigName);
                return;
            }

            var restore = BlocksContext.GetContext();
            try
            {
                BlocksContext.SetContext(TenantContext(message.TenantId));
                await SendClaimedAsync(message, cancellationToken);
            }
            finally
            {
                BlocksContext.SetContext(restore, restore is not null);
            }
        }

        private async Task SendClaimedAsync(PushRequested message, CancellationToken cancellationToken)
        {
            PreparedPush? prepared;
            try
            {
                prepared = await PrepareAsync(message, cancellationToken);
            }
            catch (Exception)
            {
                // Nothing was sent yet: without the claim a redelivery of this message can try again.
                await ReleaseClaimAsync(message);
                throw;
            }

            if (prepared is null)
                return;

            var summary = await _sender.SendAsync(prepared.Session, prepared.Notes, cancellationToken);
            _logger.LogInformation("Push {ConfigName}: {Sent} sent, {Removed} devices removed, {Failed} failed",
                message.ConfigName, summary.Sent, summary.Removed, summary.Failed);
        }

        /// <summary>Everything before the first FCM call. Null = nothing to send (logged).</summary>
        private async Task<PreparedPush?> PrepareAsync(PushRequested message, CancellationToken cancellationToken)
        {
            if (!await CanSendAsync(message, cancellationToken))
                return null;

            var composition = await _composer.PrepareAsync(message, cancellationToken);
            if (composition is null)
            {
                _logger.LogWarning("Push {ConfigName} dropped: the configuration has no push template", message.ConfigName);
                return null;
            }

            var notes = await NotesAsync(message, composition, cancellationToken);

            // Read again right before FCM: push or a platform may have been switched off since the message started.
            var settings = await _settings.GetAsync(cancellationToken);
            notes = settings is { CanSend: true } ? notes.Where(n => settings.IsOn(n.Device.Platform)).ToList() : [];
            if (settings is null || notes.Count == 0)
            {
                _logger.LogInformation("Push {ConfigName}: no registered device on a platform that is on", message.ConfigName);
                return null;
            }

            var opening = await _sender.OpenAsync(message.TenantId, settings, cancellationToken);
            if (opening.Session is null)
            {
                _logger.LogWarning("Push {ConfigName} dropped: the service account key is missing or refused ({Failure})", message.ConfigName, opening.Failure);
                return null;
            }

            return new PreparedPush(opening.Session, notes);
        }

        private async Task<bool> CanSendAsync(PushRequested message, CancellationToken cancellationToken)
        {
            var settings = await _settings.GetAsync(cancellationToken);
            if (settings is { CanSend: true })
                return true;

            _logger.LogInformation("Push {ConfigName} dropped: push is off or not set up for the tenant", message.ConfigName);
            return false;
        }

        private async Task ReleaseClaimAsync(PushRequested message)
        {
            try
            {
                await _dedup.ReleaseAsync(message.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The original failure matters more; the claim then expires with its TTL. Type only.
                _logger.LogWarning("Push {ConfigName}: the dedup claim could not be released ({ErrorType})", message.ConfigName, ex.GetType().Name); // NOSONAR S6667 — type only
            }
        }

        private async Task<List<PushNote>> NotesAsync(PushRequested message, PushComposition composition, CancellationToken cancellationToken)
        {
            var rootIds = message.RootUserIds.ToHashSet(StringComparer.Ordinal);
            var projectIds = message.UserIds.Where(id => !rootIds.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
            var seenSince = NotificationDevice.SeenSince(_timeProvider.GetUtcNow().UtcDateTime);

            var projectDb = new PushDb(message.TenantId, Root: false);
            var rootDb = new PushDb(message.TenantId, Root: true);
            var devices = (await _devices.GetForUsersAsync(projectDb, projectIds, seenSince, cancellationToken)).Select(d => (Db: projectDb, Device: d))
                .Concat((await _devices.GetForUsersAsync(rootDb, rootIds, seenSince, cancellationToken)).Select(d => (Db: rootDb, Device: d)));

            var data = new Dictionary<string, string>
            {
                ["notificationId"] = message.Id.ToString(),
                ["configName"] = message.ConfigName,
            };

            return devices
                .Where(pair => composition.Allows(pair.Device.Platform))
                .Select(pair => composition.NoteFor(pair.Db, pair.Device, data))
                .ToList();
        }

        private sealed record PreparedPush(FcmSession Session, IReadOnlyList<PushNote> Notes);

        private string? Refusal(PushRequested? message)
        {
            if (message is null || message.Id == Guid.Empty || string.IsNullOrWhiteSpace(message.TenantId) || string.IsNullOrWhiteSpace(message.ConfigName))
                return "incomplete message";
            if (message.UserIds.Count == 0)
                return "no receivers";
            if (_timeProvider.GetUtcNow().UtcDateTime - message.SentAt > MaxAge)
                return "older than one hour";
            return null;
        }

        /// <summary>No user: KV and DB reads go to <paramref name="tenantId"/>'s DB.</summary>
        private static BlocksContext TenantContext(string tenantId) => BlocksContext.Create(
            tenantId: tenantId,
            roles: [],
            userId: string.Empty,
            isAuthenticated: false,
            requestUri: string.Empty,
            organizationId: string.Empty,
            expireOn: DateTime.MinValue,
            email: string.Empty,
            permissions: [],
            userName: string.Empty,
            phoneNumber: string.Empty,
            displayName: string.Empty,
            oauthToken: string.Empty,
            originalTenantId: tenantId,
            impersonated: false);
    }
}
