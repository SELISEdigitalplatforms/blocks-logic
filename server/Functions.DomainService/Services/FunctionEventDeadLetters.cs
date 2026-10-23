using Blocks.Genesis.Entities.Logic;
using Functions.DomainService.Events;
using Functions.DomainService.Queue;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    /// <summary>Why an event was dead-lettered, as written to the stream. Fixed words, never payload text.</summary>
    public static class FunctionEventDeadReasons
    {
        public const string InvalidMessage = "INVALID_MESSAGE";
        public const string NoTenant = "NO_TENANT";
        public const string DepthLimit = "DEPTH_LIMIT";
        public const string InvokeFailed = "INVOKE_FAILED";
        public const string EventExpired = "EVENT_EXPIRED";
        public const string RunFailed = "RUN_FAILED";
    }

    public interface IFunctionEventDeadLetters
    {
        /// <summary>
        /// Adds one entry to <c>functions:dead-function-events</c>: <c>eventId, functionId, runId, tenantId, name, reason</c>.
        /// Never the payload. Best effort: a Redis failure is logged and swallowed, because the caller is a consumer that must
        /// not throw.
        /// </summary>
        Task AddAsync(string eventId, string functionId, string runId, string tenantId, string name, string reason);

        /// <summary>
        /// Called when a run closed for good without succeeding. Does nothing unless an event started the run; then it
        /// dead-letters the event for this function with the run's failure as the reason. Never throws.
        /// </summary>
        Task RunFailedAsync(string tenantId, FunctionRunEntity run);
    }

    /// <inheritdoc cref="IFunctionEventDeadLetters"/>
    public sealed class FunctionEventDeadLetters : IFunctionEventDeadLetters
    {
        private readonly ICacheClient _cache;
        private readonly IFunctionEventRunStore _runStore;
        private readonly ILogger<FunctionEventDeadLetters> _logger;

        public FunctionEventDeadLetters(
            ICacheClient cache, IFunctionEventRunStore runStore, ILogger<FunctionEventDeadLetters> logger)
        {
            _cache = cache;
            _runStore = runStore;
            _logger = logger;
        }

        public async Task AddAsync(string eventId, string functionId, string runId, string tenantId, string name, string reason)
        {
            try
            {
                await _cache.CacheDatabase().StreamAddAsync(FunctionQueueKeys.DeadFunctionEventsStream,
                [
                    new NameValueEntry("eventId", eventId ?? string.Empty),
                    new NameValueEntry("functionId", functionId ?? string.Empty),
                    new NameValueEntry("runId", runId ?? string.Empty),
                    new NameValueEntry("tenantId", tenantId ?? string.Empty),
                    new NameValueEntry("name", name ?? string.Empty),
                    new NameValueEntry("reason", reason),
                ]);
                _logger.LogWarning(
                    "Function event {EventId} ({Name}) dead-lettered for function {FunctionId}: {Reason}",
                    eventId, name, functionId, reason);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                _logger.LogError(ex,
                    "Could not dead-letter function event {EventId} for function {FunctionId} ({Reason})", eventId, functionId, reason);
            }
        }

        public async Task RunFailedAsync(string tenantId, FunctionRunEntity run)
        {
            if (run.InvokedBy != InvokedByType.Event) return;

            var info = await _runStore.GetAsync(run.ItemId);
            var expired = run.ErrorMessage?.StartsWith(FunctionQueueKeys.Wire.EventExpired, StringComparison.Ordinal) == true;
            var reason = expired
                ? FunctionEventDeadReasons.EventExpired
                : $"{FunctionEventDeadReasons.RunFailed}:{run.Status}/{run.ErrorCode}";
            await AddAsync(
                run.InvokedById ?? info?.EventId ?? string.Empty, run.FunctionId, run.ItemId, tenantId, info?.Name ?? string.Empty, reason);
        }
    }
}
