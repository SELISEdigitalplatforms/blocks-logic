using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>One push for one device, with the DB the device row lives in.</summary>
    public sealed record PushNote(PushDb Db, NotificationDevice Device, string Title, string Body, IReadOnlyDictionary<string, string> Data);

    /// <summary>How a batch went. <see cref="Removed"/>: devices deleted because Firebase refused their token.</summary>
    public sealed record PushSendSummary(int Sent, int Removed, int Failed)
    {
        public static PushSendSummary AllFailed(int count) => new(0, 0, count);
    }

    /// <summary>Sends pushes through FCM for one tenant.</summary>
    public interface IFcmPushSender
    {
        Task<PushSendSummary> SendAsync(string tenantId, PushSettingsRecord settings, IReadOnlyList<PushNote> notes, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IFcmPushSender"/>
    /// <remarks>
    /// Key from Blocks Secrets, access token (cached), then one FCM call per device, up to 8 at a time. A refused token
    /// deletes the device. 429/5xx is retried up to <see cref="MaxRetries"/> times with backoff (Retry-After honoured,
    /// at most <see cref="MaxRetryWait"/>); 401 renews the access token once. Logs hold counts and status codes only:
    /// never tokens, user ids, texts or the key.
    /// </remarks>
    public sealed class FcmPushSender : IFcmPushSender
    {
        public const int MaxRetries = 3;
        internal static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);
        private const int Parallelism = 8;

        private readonly IPushCredentialStore _credentials;
        private readonly IFcmAccessTokenProvider _tokens;
        private readonly IFcmClient _client;
        private readonly IPushDeviceStore _devices;
        private readonly IPushRetryDelay _delay;
        private readonly ILogger<FcmPushSender> _logger;

        public FcmPushSender(IPushCredentialStore credentials, IFcmAccessTokenProvider tokens, IFcmClient client,
            IPushDeviceStore devices, IPushRetryDelay delay, ILogger<FcmPushSender> logger)
        {
            _credentials = credentials;
            _tokens = tokens;
            _client = client;
            _devices = devices;
            _delay = delay;
            _logger = logger;
        }

        public async Task<PushSendSummary> SendAsync(string tenantId, PushSettingsRecord settings, IReadOnlyList<PushNote> notes,
            CancellationToken cancellationToken)
        {
            if (notes.Count == 0)
                return new PushSendSummary(0, 0, 0);

            var key = await KeyAsync(tenantId, settings, cancellationToken);
            if (key is null)
                return PushSendSummary.AllFailed(notes.Count);

            // One token request before the parallel sends (they then all hit the cache).
            if (await _tokens.GetTokenAsync(tenantId, key, cancellationToken) is null)
                return PushSendSummary.AllFailed(notes.Count);

            var sent = 0;
            var removed = 0;
            var failed = 0;
            await Parallel.ForEachAsync(notes, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
                async (note, token) =>
                {
                    var outcome = await SendOneAsync(tenantId, settings.ProjectId, key, note, token);
                    switch (outcome)
                    {
                        case FcmOutcome.Sent:
                            Interlocked.Increment(ref sent);
                            break;
                        case FcmOutcome.InvalidToken:
                            await _devices.DeleteAsync(note.Db, note.Device.Id, token);
                            Interlocked.Increment(ref removed);
                            break;
                        default:
                            Interlocked.Increment(ref failed);
                            break;
                    }
                });

            return new PushSendSummary(sent, removed, failed);
        }

        private async Task<ServiceAccountKey?> KeyAsync(string tenantId, PushSettingsRecord settings, CancellationToken cancellationToken)
        {
            var json = await _credentials.ReadAsync(tenantId, settings.SecretName, cancellationToken);
            var key = ServiceAccountKey.TryParse(json, out _);
            if (key is null)
            {
                _logger.LogWarning("Push: the service account key is missing or not valid; nothing sent");
            }

            return key;
        }

        private async Task<FcmOutcome> SendOneAsync(string tenantId, string projectId, ServiceAccountKey key, PushNote note, CancellationToken cancellationToken)
        {
            var message = new FcmMessage(note.Device.Token, note.Title, note.Body, note.Data);
            var renewedToken = false;
            FcmSendResult? last = null;
            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                var accessToken = await _tokens.GetTokenAsync(tenantId, key, cancellationToken);
                if (accessToken is null)
                    return FcmOutcome.Failed;

                last = await _client.SendAsync(projectId, accessToken, message, cancellationToken);
                if (last.Outcome == FcmOutcome.AuthFailed && !renewedToken)
                {
                    _tokens.Forget(tenantId);
                    renewedToken = true;
                    continue;
                }

                if (last.Outcome != FcmOutcome.Retry || attempt == MaxRetries)
                    break;

                await _delay.DelayAsync(Backoff(attempt, last.RetryAfter), cancellationToken);
            }

            if (last!.Outcome is FcmOutcome.Retry or FcmOutcome.AuthFailed or FcmOutcome.Failed)
            {
                // Status and FCM code only: never the token, the texts or the response body. The device is kept.
                _logger.LogWarning("Push: one message dropped, device kept (HTTP {Status}, {Outcome}, {ErrorCode})", last.Status, last.Outcome, last.ErrorCode ?? "none");
            }

            return last.Outcome;
        }

        /// <summary>1 s, 2 s, 4 s, or the server's Retry-After; never more than <see cref="MaxRetryWait"/>.</summary>
        public static TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
        {
            var wait = retryAfter is { } serverWait && serverWait > TimeSpan.Zero
                ? serverWait
                : TimeSpan.FromSeconds(Math.Pow(2, attempt));
            return wait > MaxRetryWait ? MaxRetryWait : wait;
        }
    }
}
