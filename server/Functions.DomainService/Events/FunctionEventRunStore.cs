using System.Text.Json;
using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Events
{
    /// <summary>What a run started by a function event remembers about that event. No payload.</summary>
    /// <param name="EventId">The event's id (the run's <c>InvokedById</c>).</param>
    /// <param name="Name">The event's name, for the dead-letter entry. The function sees it in <c>ctx.run.invokedBy.name</c>.</param>
    /// <param name="Depth">The event's own <see cref="FunctionEvent.Depth"/>: the events this run sends are one deeper.</param>
    public sealed record FunctionEventRunInfo(string EventId, string Name, int Depth);

    /// <summary>
    /// Logic-owned storage of <see cref="FunctionEventRunInfo"/>, keyed by run id. Genesis' run entity has no place for it
    /// and is not changed; a Redis key with a TTL longer than the run's payload (see
    /// <see cref="FunctionWorkerQueueKeys.EventRunInfoTtl"/>) is the simplest thing that lives exactly as long as the
    /// run can still act. A missing key reads as "unknown": the depth then falls back to the one-hop default.
    /// </summary>
    public interface IFunctionEventRunStore
    {
        /// <summary>Writes the info. Throws when Redis does, so the caller does not create a run it cannot track.</summary>
        Task SaveAsync(string runId, FunctionEventRunInfo info);

        /// <summary>The info of a run, or null when there is none (not an event run, expired, or Redis is down).</summary>
        Task<FunctionEventRunInfo?> GetAsync(string runId);
    }

    public sealed class FunctionEventRunStore : IFunctionEventRunStore
    {
        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionEventRunStore> _logger;

        public FunctionEventRunStore(ICacheClient cache, ILogger<FunctionEventRunStore> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task SaveAsync(string runId, FunctionEventRunInfo info)
        {
            await _cache.CacheDatabase().StringSetAsync(
                FunctionWorkerQueueKeys.EventRunInfo(runId), JsonSerializer.Serialize(info), FunctionWorkerQueueKeys.EventRunInfoTtl);
        }

        public async Task<FunctionEventRunInfo?> GetAsync(string runId)
        {
            try
            {
                var raw = await _cache.CacheDatabase().StringGetAsync(FunctionWorkerQueueKeys.EventRunInfo(runId));
                return raw.IsNullOrEmpty ? null : JsonSerializer.Deserialize<FunctionEventRunInfo>((string)raw!);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
            {
                _logger.LogWarning("Could not read the event info of run {RunId} ({ExceptionType})", runId, ex.GetType().Name);
                return null;
            }
        }
    }
}
