using System.Text;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
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
        /// <param name="invokedBy">Only <see cref="InvokedByType.Http"/> calls are rate limited (FN-19).</param>
        /// <param name="version">The deployed version being called: its trigger and limits are the live ones.</param>
        Task<AdmissionResult> AdmitAsync(
            FunctionEntity function, string tenantId, string? inputJson, InvokedByType invokedBy, FunctionVersionEntity? version,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Input caps, and the HTTP rate limit (FN-19, decided 2026-10-07).
    /// <para>
    /// Only HTTP calls are counted, per function per minute (a function's id cannot be faked; a
    /// caller's IP behind a proxy can). A Public function gets
    /// <c>Functions:RateLimits:PublicPerMinute</c> (600) unless its tenant set its own; a Token
    /// one has no limit unless its tenant set one. Workflow and Test calls are never counted.
    /// <c>Functions:RateLimits:Enabled=false</c> switches every limit off. A refused call
    /// creates no run and writes nothing.
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
        private readonly int _publicPerMinute;

        /// <summary>HTTP calls per minute a Public function gets when its tenant set none (FN-19).</summary>
        public const int DefaultPublicPerMinute = 600;
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
            // On by default since 2026-10-07 (FN-19); false switches every limit off at once.
            _rateLimitsEnabled = configuration.GetValue("Functions:RateLimits:Enabled", true);
            _publicPerMinute = Math.Max(0, configuration.GetValue("Functions:RateLimits:PublicPerMinute", DefaultPublicPerMinute));
            if (!_rateLimitsEnabled)
            {
                _logger.LogWarning("Functions rate limiting is OFF: public functions can be called without limit.");
            }
        }

        /// <summary>An HTTP call with the function's own settings: the shape older callers and tests use.</summary>
        public Task<AdmissionResult> AdmitAsync(
            FunctionEntity function, string tenantId, string? inputJson, CancellationToken cancellationToken = default)
            => AdmitAsync(function, tenantId, inputJson, InvokedByType.Http, null, cancellationToken);

        public async Task<AdmissionResult> AdmitAsync(
            FunctionEntity function, string tenantId, string? inputJson, InvokedByType invokedBy, FunctionVersionEntity? version,
            CancellationToken cancellationToken = default)
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
            // Only HTTP calls are counted: a workflow or a Test is the tenant's own traffic.
            if (!_rateLimitsEnabled || invokedBy != InvokedByType.Http)
            {
                return AdmissionResult.Admit();
            }

            // The live settings are the deployed version's; the draft's only when none is passed.
            var stored = (version?.Limits ?? function.Limits) ?? new FunctionLimits();
            var trigger = version?.Trigger ?? function.Trigger;
            // Public and nothing set → the platform default; Token and nothing set → no limit.
            var perMinute = stored.RequestsPerMinute is > 0
                ? stored.RequestsPerMinute
                : trigger?.AuthMode == AuthMode.Public && _publicPerMinute > 0 ? _publicPerMinute : (int?)null;
            var limits = new FunctionLimits { RequestsPerMinute = perMinute, RequestsPerDay = stored.RequestsPerDay };

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
