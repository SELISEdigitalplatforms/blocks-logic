using System.Text;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>Why an invocation was refused, if it was.</summary>
    public enum AdmissionOutcome
    {
        /// <summary>Proceed. The only outcome V1 ever produces for volume.</summary>
        Admitted = 0,

        /// <summary>The input exceeded the 1 MB ceiling.</summary>
        InputTooLarge = 1,

        /// <summary>Requests-per-minute exceeded. Only reachable with rate limiting switched on; thrown as <see cref="Utils.FunctionRateLimitedException"/>, never returned.</summary>
        RateLimited = 2,

        /// <summary>Requests-per-day exceeded (per function). Only reachable with rate limiting switched on; thrown, never returned.</summary>
        QuotaExceeded = 3,
    }

    /// <summary>The verdict, plus what to tell the caller.</summary>
    public sealed record AdmissionResult(AdmissionOutcome Outcome, string? Message = null, int? RetryAfterSeconds = null)
    {
        public bool IsAdmitted => Outcome == AdmissionOutcome.Admitted;

        public static AdmissionResult Admit() => new(AdmissionOutcome.Admitted);
    }

    /// <summary>Decides whether an invocation may create a run.</summary>
    public interface IFunctionAdmissionService
    {
        /// <summary>
        /// Returns the verdict for a request that is <i>not</i> a volume refusal (admitted, or
        /// input too large). A volume refusal — <see cref="AdmissionOutcome.RateLimited"/> or
        /// <see cref="AdmissionOutcome.QuotaExceeded"/> — is thrown as
        /// <see cref="FunctionRateLimitedException"/> instead, carrying the Retry-After, so it
        /// reaches every entry point (HTTP, Test, Replay, workflow) as a 429-shaped error rather
        /// than being rewrapped by the caller as a 400 validation failure.
        /// </summary>
        /// <exception cref="FunctionRateLimitedException">A per-minute or per-day limit refused the call.</exception>
        Task<AdmissionResult> AdmitAsync(FunctionEntity function, string tenantId, string? inputJson, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Input caps, and rate limits that are switched off.
    /// <para>
    /// The product decision (DECISIONS.md) is that <b>nothing is refused for volume in V1</b>.
    /// Requests-per-minute and per-day exist in the model and the API because the shape will be
    /// needed later, but they are hidden in the interface and disabled by configuration, and
    /// this service returns 429 only when <c>Functions:RateLimits:Enabled</c> is true
    /// <i>and</i> the function itself sets a value. Both are false and null by default, so the
    /// normal path touches no counters at all.
    /// </para>
    /// <para>
    /// Concurrency is deliberately not checked here. It is a <i>scheduling</i> limit: the run is
    /// created and enqueued regardless, and the runner's per-function semaphore decides when it
    /// starts. Overflow queues; it never rejects. Checking it here would turn a queue into an
    /// error, which is exactly what the decision forbids.
    /// </para>
    /// <para>
    /// The one hard refusal is input size, and that is not about volume: a payload over 1 MB
    /// cannot be delivered to a sandbox at all, so accepting it would create a run that could
    /// only fail.
    /// </para>
    /// </summary>
    public class FunctionAdmissionService : IFunctionAdmissionService
    {
        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionAdmissionService> _logger;
        private readonly bool _rateLimitsEnabled;
        private readonly Func<DateTime> _clock;

        public FunctionAdmissionService(
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionAdmissionService> logger)
            : this(cache, configuration, logger, () => DateTime.UtcNow)
        {
        }

        /// <summary>Test seam: a fixed UTC clock, so window keys and Retry-After are assertable.</summary>
        internal FunctionAdmissionService(
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionAdmissionService> logger,
            Func<DateTime> clock)
        {
            _cache = cache;
            _logger = logger;
            _clock = clock;
            _rateLimitsEnabled = configuration.GetValue("Functions:RateLimits:Enabled", false);

            if (_rateLimitsEnabled)
            {
                _logger.LogWarning(
                    "Functions rate limiting is ENABLED. V1 ships with it off; callers can now receive 429.");
            }
        }

        public async Task<AdmissionResult> AdmitAsync(
            FunctionEntity function, string tenantId, string? inputJson, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(function);

            if (inputJson is not null)
            {
                var bytes = Encoding.UTF8.GetByteCount(inputJson);
                if (bytes > FunctionLimits.Ceiling.InputBytes)
                {
                    return new AdmissionResult(
                        AdmissionOutcome.InputTooLarge,
                        $"the input is {bytes} bytes, over the {FunctionLimits.Ceiling.InputBytes} byte limit");
                }
            }

            // The switch and the per-function value must BOTH be set. Either one absent means
            // no counter is read or written — the default path costs nothing and refuses nothing.
            if (!_rateLimitsEnabled)
            {
                return AdmissionResult.Admit();
            }

            var limits = function.Limits ?? new FunctionLimits();

            // One clock reading for both the window key and its Retry-After, so a request that
            // lands on a minute boundary cannot be counted in one window and told to wait for
            // the end of the next.
            var now = _clock();

            if (limits.RequestsPerMinute is > 0)
            {
                var refused = await ExceedsAsync(
                    FunctionQueueKeys.RateMinute(function.ItemId, now),
                    limits.RequestsPerMinute.Value,
                    TimeSpan.FromMinutes(2));

                if (refused)
                {
                    throw Refusal(
                        new AdmissionResult(
                            AdmissionOutcome.RateLimited,
                            $"this function allows {limits.RequestsPerMinute} requests per minute",
                            RetryAfterSeconds: SecondsUntil(now, now.Date.AddHours(now.Hour).AddMinutes(now.Minute + 1))),
                        function);
                }
            }

            if (limits.RequestsPerDay is > 0)
            {
                // Per function per day, like the per-minute window. The limit is a per-function
                // setting, so a per-tenant counter would have let one function's traffic spend
                // another's allowance (and the smallest limit in the tenant win for everyone).
                var refused = await ExceedsAsync(
                    FunctionQueueKeys.QuotaDay(tenantId, function.ItemId, now),
                    limits.RequestsPerDay.Value,
                    TimeSpan.FromDays(2));

                if (refused)
                {
                    throw Refusal(
                        new AdmissionResult(
                            AdmissionOutcome.QuotaExceeded,
                            $"this function allows {limits.RequestsPerDay} requests per day",
                            RetryAfterSeconds: SecondsUntil(now, now.Date.AddDays(1))),
                        function);
                }
            }

            return AdmissionResult.Admit();
        }

        private FunctionRateLimitedException Refusal(AdmissionResult result, FunctionEntity function)
        {
            _logger.LogInformation(
                "Refusing an invocation of {FunctionId}: {Outcome}, retry after {RetryAfter}s",
                function.ItemId, result.Outcome, result.RetryAfterSeconds);
            return new FunctionRateLimitedException(result.Message!, result.RetryAfterSeconds ?? 1);
        }

        private static int SecondsUntil(DateTime now, DateTime rollover) =>
            Math.Max(1, (int)Math.Ceiling((rollover - now).TotalSeconds));

        /// <summary>
        /// Increments a window counter and reports whether it has gone past the limit. The TTL
        /// is set only on first increment, so a busy window cannot keep extending its own life.
        /// </summary>
        private async Task<bool> ExceedsAsync(string key, int limit, TimeSpan ttl)
        {
            try
            {
                var database = _cache.CacheDatabase();
                var current = await database.StringIncrementAsync(key);
                if (current == 1)
                {
                    await database.KeyExpireAsync(key, ttl);
                }
                return current > limit;
            }
            catch (Exception ex)
            {
                // Redis being unavailable must not turn into a refusal. Failing open is the
                // right call for a limiter that is switched off by default anyway: the
                // alternative is refusing traffic because a counter could not be written.
                _logger.LogWarning(
                    "Could not evaluate the rate limit at {Key}; admitting the request. {Message}",
                    key, ex.Message);
                return false;
            }
        }
    }
}
