using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Proxy.DomainService.Services
{
    /// <summary>Decides whether one gateway call may go through, by the proxy's calls-per-minute limit.</summary>
    public interface IProxyRateLimiter
    {
        /// <summary>
        /// Counts one gateway call against <paramref name="config"/>'s limit. Returns <c>null</c> when the call
        /// may go on, or the seconds until the window resets (the <c>Retry-After</c>) when it is refused.
        /// </summary>
        Task<int?> TryAcquireAsync(string tenantId, ProxyResolvedConfig config, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The gateway rate limit (P-3, 2026-10-07). The same rule as functions (FN-19): counted per proxy per
    /// minute, never per caller IP — behind a load balancer every caller can look like one IP, and a forwarded
    /// IP can be faked. A Public proxy gets <c>Proxy:RateLimits:PublicPerMinute</c> (600) unless its tenant set
    /// its own; a token proxy has no limit unless its tenant set one. Only client calls through the gateway
    /// route are counted: workflow steps and Test calls never reach this class.
    /// <c>Proxy:RateLimits:Enabled=false</c> switches every limit off.
    /// <para>
    /// The counter lives in Redis, so the limit holds across every Api instance. A fixed one-minute window:
    /// simple, and the worst case (2× the limit across a window edge) is still bounded.
    /// </para>
    /// </summary>
    public sealed class ProxyRateLimiter : IProxyRateLimiter
    {
        /// <summary>Gateway calls per minute a Public proxy gets when its tenant set none.</summary>
        public const int DefaultPublicPerMinute = 600;

        private readonly ICacheClient _cache;
        private readonly ILogger<ProxyRateLimiter> _logger;
        private readonly bool _enabled;
        private readonly int _publicPerMinute;
        private readonly TimeProvider _timeProvider;

        public ProxyRateLimiter(
            ICacheClient cache,
            IConfiguration configuration,
            TimeProvider timeProvider,
            ILogger<ProxyRateLimiter> logger)
        {
            _cache = cache;
            _logger = logger;
            _timeProvider = timeProvider;
            _enabled = configuration.GetValue("Proxy:RateLimits:Enabled", true);
            _publicPerMinute = Math.Max(0, configuration.GetValue("Proxy:RateLimits:PublicPerMinute", DefaultPublicPerMinute));
            if (!_enabled)
            {
                _logger.LogWarning("Proxy rate limiting is OFF: public proxies can be called without limit.");
            }
        }

        /// <summary>The limit that applies, or <c>null</c> for none. Public and nothing set → the platform default.</summary>
        internal int? EffectiveLimit(ProxyResolvedConfig config) =>
            config.RequestsPerMinute is > 0
                ? config.RequestsPerMinute
                : config.Access.IsPublic && _publicPerMinute > 0 ? _publicPerMinute : null;

        public async Task<int?> TryAcquireAsync(
            string tenantId, ProxyResolvedConfig config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!_enabled || EffectiveLimit(config) is not { } limit)
            {
                return null;
            }

            // One clock reading for the window key and its Retry-After, so a call on a minute boundary is
            // never counted in one window and told to wait for the end of the next.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var key = $"blocks-logic:proxy:rate:{tenantId}:{config.ProxyId}:{now:yyyyMMddHHmm}";

            long count;
            try
            {
                var database = _cache.CacheDatabase();
                count = await database.StringIncrementAsync(key);
                if (count == 1)
                {
                    // Set only on the first call, so a busy window cannot keep extending its own life.
                    await database.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
                }
            }
            catch (Exception ex)
            {
                // Redis down must not take every proxy down with it. Fail open, the same as functions.
                _logger.LogWarning(
                    "Could not evaluate the proxy rate limit at {Key}; letting the call through. {Message}",
                    key, ex.Message);
                return null;
            }

            if (count <= limit)
            {
                return null;
            }

            var nextMinute = now.Date.AddHours(now.Hour).AddMinutes(now.Minute + 1);
            var retryAfter = Math.Max(1, (int)Math.Ceiling((nextMinute - now).TotalSeconds));

            // Log the first refusal of a window only: a flood must not turn into a flood of log lines.
            if (count == limit + 1)
            {
                _logger.LogWarning(
                    "Proxy {ProxyId} (slug {Slug}, tenant {TenantId}) hit its limit of {Limit} calls per minute; refusing until the window resets.",
                    config.ProxyId, config.Slug, tenantId, limit);
            }

            return retryAfter;
        }
    }
}
