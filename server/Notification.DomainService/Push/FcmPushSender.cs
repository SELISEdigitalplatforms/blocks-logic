using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>One push for one device, with the DB the device row lives in. <see cref="Blocks"/>: per-platform options.</summary>
    public sealed record PushNote(PushDb Db, NotificationDevice Device, string Title, string Body, IReadOnlyDictionary<string, string> Data,
        PushPlatformBlocks? Blocks = null);

    /// <summary>What happened to one note. <see cref="Removed"/>: the device was deleted because Firebase refused its token.</summary>
    public sealed record PushNoteResult(PushNote Note, FcmSendResult Result, bool Removed);

    /// <summary>How a batch went, with one result per note in the order of the notes.</summary>
    public sealed record PushSendSummary(int Sent, int Removed, int Failed, IReadOnlyList<PushNoteResult> Results);

    /// <summary>A tenant's key, checked and with an access token issued: ready to send. Never logged.</summary>
    public sealed record FcmSession(string TenantId, ServiceAccountKey Key);

    public enum FcmOpenFailure
    {
        None,

        /// <summary>No key stored, or the stored value is not a valid service-account key.</summary>
        NoKey,

        /// <summary>Google refused to issue an access token for the key.</summary>
        KeyRefused,
    }

    /// <summary>Result of <see cref="IFcmPushSender.OpenAsync"/>: a session, or why there is none.</summary>
    public sealed record FcmOpening(FcmSession? Session, FcmOpenFailure Failure);

    /// <summary>The push key could not be read from Blocks Secrets (a store failure, not a missing key). Holds no detail.</summary>
    public sealed class PushKeyUnavailableException : Exception
    {
        public PushKeyUnavailableException() : base("The push key could not be read.")
        {
        }

        public PushKeyUnavailableException(string message) : base(message)
        {
        }

        public PushKeyUnavailableException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>Sends pushes through FCM for one tenant: open a session (key + access token), then send.</summary>
    public interface IFcmPushSender
    {
        /// <summary>
        /// Reads the key (by <c>SecretId</c>, else by name) and gets an access token. Throws
        /// <see cref="PushKeyUnavailableException"/> when Blocks Secrets fails (logged by type only).
        /// </summary>
        Task<FcmOpening> OpenAsync(string tenantId, PushSettingsRecord settings, CancellationToken cancellationToken);

        Task<PushSendSummary> SendAsync(FcmSession session, IReadOnlyList<PushNote> notes, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IFcmPushSender"/>
    /// <remarks>
    /// One FCM call per device, up to 8 at a time, to the project named in the key itself (never the KV project id, so
    /// a KV/key mismatch cannot send to the wrong project). A refused token deletes the device. 429/5xx is retried up
    /// to <see cref="MaxRetries"/> times with backoff (Retry-After honoured, at most <see cref="MaxRetryWait"/>); 401
    /// renews the access token once. Logs hold counts, types and status codes only: never tokens, user ids, texts or keys.
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

        public Task<FcmOpening> OpenAsync(string tenantId, PushSettingsRecord settings, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return OpenCoreAsync(tenantId, settings, cancellationToken);
        }

        private async Task<FcmOpening> OpenCoreAsync(string tenantId, PushSettingsRecord settings, CancellationToken cancellationToken)
        {
            var key = ServiceAccountKey.TryParse(await ReadKeyAsync(tenantId, settings, cancellationToken), out _);
            if (key is null)
            {
                _logger.LogWarning("Push: the service account key is missing or not valid; nothing sent");
                return new FcmOpening(null, FcmOpenFailure.NoKey);
            }

            // One token request before the parallel sends (they then all hit the cache).
            if (await _tokens.GetTokenAsync(tenantId, key, cancellationToken) is null)
                return new FcmOpening(null, FcmOpenFailure.KeyRefused);

            return new FcmOpening(new FcmSession(tenantId, key), FcmOpenFailure.None);
        }

        private async Task<string?> ReadKeyAsync(string tenantId, PushSettingsRecord settings, CancellationToken cancellationToken)
        {
            try
            {
                return await _credentials.ReadAsync(tenantId, settings.SecretId, settings.SecretName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Type only: a store error message can name the secret. The caller decides to retry.
                _logger.LogWarning("Push: the service account key could not be read ({ErrorType}); nothing sent", ex.GetType().Name); // NOSONAR S6667 — type only
                throw new PushKeyUnavailableException();
            }
        }

        public async Task<PushSendSummary> SendAsync(FcmSession session, IReadOnlyList<PushNote> notes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            var results = new PushNoteResult[notes.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, notes.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var note = notes[index];
                    var result = await SendOneAsync(session, note, token);
                    var removed = result.Outcome == FcmOutcome.InvalidToken;
                    if (removed)
                        await _devices.DeleteAsync(note.Db, note.Device.Id, token);
                    results[index] = new PushNoteResult(note, result, removed);
                });

            var sent = results.Count(r => r.Result.Outcome == FcmOutcome.Sent);
            var removedCount = results.Count(r => r.Removed);
            return new PushSendSummary(sent, removedCount, results.Length - sent - removedCount, results);
        }

        private async Task<FcmSendResult> SendOneAsync(FcmSession session, PushNote note, CancellationToken cancellationToken)
        {
            var message = new FcmMessage(note.Device.Token, note.Device.Platform, note.Title, note.Body, note.Data, note.Blocks);
            var renewedToken = false;
            FcmSendResult? last = null;
            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                var accessToken = await _tokens.GetTokenAsync(session.TenantId, session.Key, cancellationToken);
                if (accessToken is null)
                    return new FcmSendResult(FcmOutcome.AuthFailed, 0);

                last = await _client.SendAsync(session.Key.ProjectId, accessToken, message, cancellationToken);
                if (last.Outcome == FcmOutcome.AuthFailed && !renewedToken)
                {
                    _tokens.Forget(session.TenantId);
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

            return last;
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
