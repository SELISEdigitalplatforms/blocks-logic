using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Reuses access tokens, and coalesces concurrent refreshes for one key into one acquisition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owned entirely by the Office 365 strategy. No other sender resolves it, and no inbound
    /// poller has any reason to: coupling another provider's credentials to this cache would make
    /// one provider's refresh storm another's outage.
    /// </para>
    /// <para>
    /// Only access tokens are held. The client secret is resolved from the vault immediately
    /// before each acquisition and never kept, so a rotation under the same reference is picked up
    /// at the next refresh without any invalidation event — and a process dump of this cache
    /// yields credentials that expire within the hour rather than ones that do not.
    /// </para>
    /// </remarks>
    public sealed class CachingOffice365TokenProvider : Office365TokenProvider
    {
        private readonly ConcurrentDictionary<Office365TokenCacheKey, CacheEntry> _cache = new();
        private readonly ConcurrentDictionary<Office365TokenCacheKey, Lazy<Task<Office365AccessToken>>> _inFlight = new();
        private readonly ISystemClock _clock;
        private readonly Office365TokenCacheOptions _options;
        private readonly ILogger<CachingOffice365TokenProvider> _logger;

        public CachingOffice365TokenProvider(
            IServiceScopeFactory scopeFactory,
            IOffice365TokenAcquirer acquirer,
            ISystemClock clock,
            IOptions<Office365TokenCacheOptions> options,
            ILogger<CachingOffice365TokenProvider> logger)
            : base(scopeFactory, acquirer)
        {
            _clock = clock;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>Completed entries currently held. For tests and diagnostics.</summary>
        internal int CachedEntryCount => _cache.Count;

        public override async Task<string> GetTokenAsync(
            Office365TokenRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var key = Office365TokenCacheKey.From(request);

            if (TryReuse(key, out var cached))
            {
                return cached;
            }

            var token = await AcquireCoalescedAsync(key, request, cancellationToken).ConfigureAwait(false);
            return token.Token;
        }

        private bool TryReuse(Office365TokenCacheKey key, out string token)
        {
            token = string.Empty;

            if (!_cache.TryGetValue(key, out var entry))
            {
                return false;
            }

            if (_clock.UtcNow >= entry.RefreshOnUtc)
            {
                return false;
            }

            entry.Touch(_clock.UtcNow);
            token = entry.Token;
            return true;
        }

        /// <summary>
        /// One acquisition per key, however many callers arrive at once.
        /// </summary>
        /// <remarks>
        /// <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> is
        /// what makes the coalescing exact: every caller that reaches the same key awaits the same
        /// task, and the factory runs once even under a burst. The entry is removed in a finally so
        /// a failed acquisition is never the thing later callers join.
        /// <para>
        /// Each caller awaits with its own cancellation token, so one caller giving up abandons its
        /// own wait and not the shared work the others are still depending on.
        /// </para>
        /// </remarks>
        private async Task<Office365AccessToken> AcquireCoalescedAsync(
            Office365TokenCacheKey key,
            Office365TokenRequest request,
            CancellationToken cancellationToken)
        {
            var lazy = _inFlight.GetOrAdd(
                key,
                _ => new Lazy<Task<Office365AccessToken>>(
                    () => AcquireAndCacheAsync(key, request),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Removed whether it succeeded or failed: a successful token now lives in the
                // cache, and a failed one must not be handed to the next independent send.
                _inFlight.TryRemove(new KeyValuePair<Office365TokenCacheKey, Lazy<Task<Office365AccessToken>>>(key, lazy));
            }
        }

        private async Task<Office365AccessToken> AcquireAndCacheAsync(Office365TokenCacheKey key, Office365TokenRequest request)
        {
            using var timeout = new CancellationTokenSource(_options.AcquisitionTimeout);

            var token = await AcquireAsync(request, timeout.Token).ConfigureAwait(false);

            // Cached only after success, so a failure never displaces a token that still works.
            var now = _clock.UtcNow;
            _cache[key] = new CacheEntry(token.Token, token.ExpiresOnUtc - _options.RefreshWindow, now);

            Evict();

            return token;
        }

        /// <summary>
        /// Keeps completed entries under the configured bound, oldest use first.
        /// </summary>
        /// <remarks>
        /// In-flight acquisitions are a different map and are never evicted: dropping one would
        /// strand every caller already awaiting it. Evicting a live token only costs an earlier
        /// reacquisition.
        /// </remarks>
        private void Evict()
        {
            if (_cache.Count <= _options.MaxEntries)
            {
                return;
            }

            var surplus = _cache
                .OrderBy(pair => pair.Value.LastUsedUtc)
                .Take(_cache.Count - _options.MaxEntries)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in surplus)
            {
                _cache.TryRemove(key, out _);
            }

            _logger.LogDebug("Office 365 token cache evicted {Count} completed entries.", surplus.Count);
        }

        private sealed class CacheEntry(string token, DateTimeOffset refreshOnUtc, DateTimeOffset lastUsedUtc)
        {
            public string Token { get; } = token;

            /// <summary>When the token stops being reusable — expiry less the refresh window.</summary>
            public DateTimeOffset RefreshOnUtc { get; } = refreshOnUtc;

            public DateTimeOffset LastUsedUtc { get; private set; } = lastUsedUtc;

            public void Touch(DateTimeOffset now) => LastUsedUtc = now;
        }
    }
}
