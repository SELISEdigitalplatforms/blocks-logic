using System.Text;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
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

        /// <summary>Requests-per-minute exceeded. Only reachable with rate limiting switched on.</summary>
        RateLimited = 2,

        /// <summary>Requests-per-day exceeded. Only reachable with rate limiting switched on.</summary>
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

        public FunctionAdmissionService(
            ICacheClient cache,
            IConfiguration configuration,
            ILogger<FunctionAdmissionService> logger)
        {
            _cache = cache;
            _logger = logger;
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

            if (limits.RequestsPerMinute is > 0)
            {
                var refused = await ExceedsAsync(
                    FunctionQueueKeys.RateMinute(function.ItemId, DateTime.UtcNow),
                    limits.RequestsPerMinute.Value,
                    TimeSpan.FromMinutes(2));

                if (refused)
                {
                    return new AdmissionResult(
                        AdmissionOutcome.RateLimited,
                        $"this function allows {limits.RequestsPerMinute} requests per minute",
                        RetryAfterSeconds: 60 - DateTime.UtcNow.Second);
                }
            }

            if (limits.RequestsPerDay is > 0)
            {
                var refused = await ExceedsAsync(
                    FunctionQueueKeys.QuotaDay(tenantId, DateTime.UtcNow),
                    limits.RequestsPerDay.Value,
                    TimeSpan.FromDays(2));

                if (refused)
                {
                    return new AdmissionResult(
                        AdmissionOutcome.QuotaExceeded,
                        $"this tenant allows {limits.RequestsPerDay} function requests per day");
                }
            }

            return AdmissionResult.Admit();
        }

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
