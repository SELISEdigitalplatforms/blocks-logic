using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Workflow.DomainService.Repositories;

namespace Workflow.DomainService.Actions
{
    /// <summary>
    /// One execution per action event and workflow. Genesis may deliver the same event more than once (a redelivery, or
    /// once per enabled logic queue), possibly at the same moment. A short Redis lock serializes two deliveries that arrive
    /// together; the stored execution (found by the event id) answers every later one and survives a crash.
    /// </summary>
    public interface IActionStartGuard
    {
        /// <summary>
        /// A held claim when this delivery may start the workflow; null when the event already started it or another
        /// delivery is starting it now. Dispose the claim after the start.
        /// </summary>
        Task<IAsyncDisposable?> TryClaimAsync(string tenantId, string workflowId, string eventId);
    }

    /// <inheritdoc cref="IActionStartGuard"/>
    public sealed class ActionStartGuard : IActionStartGuard
    {
        /// <summary>Longest one start may hold the lock; it expires on its own after a crash.</summary>
        internal static readonly TimeSpan LockLifetime = TimeSpan.FromMinutes(2);

        private readonly ICacheClient _cache;
        private readonly IWorkflowExecutionRepository _executions;
        private readonly ILogger<ActionStartGuard> _logger;

        public ActionStartGuard(ICacheClient cache, IWorkflowExecutionRepository executions, ILogger<ActionStartGuard> logger)
        {
            _cache = cache;
            _executions = executions;
            _logger = logger;
        }

        /// <summary>The Redis key of one start; the tenant is part of it, so tenants never share a lock.</summary>
        public static string LockKey(string tenantId, string workflowId, string eventId) =>
            $"logic:action-start:{tenantId}:{workflowId}:{eventId}";

        public async Task<IAsyncDisposable?> TryClaimAsync(string tenantId, string workflowId, string eventId)
        {
            var database = _cache.CacheDatabase();
            var key = LockKey(tenantId, workflowId, eventId);
            if (!await database.StringSetAsync(key, "1", LockLifetime, When.NotExists))
            {
                return null;
            }

            var claim = new Claim(database, key, _logger);
            bool started;
            try
            {
                started = await _executions.ExistsForActionAsync(tenantId, workflowId, eventId);
            }
            catch
            {
                await claim.DisposeAsync();
                throw;
            }

            if (started)
            {
                await claim.DisposeAsync();
                return null;
            }

            return claim;
        }

        private sealed class Claim(IDatabase database, string key, ILogger logger) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await database.KeyDeleteAsync(key);
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    // It expires on its own.
                    logger.LogDebug(ex, "Could not release the action start lock {Key}", key);
                }
            }
        }
    }
}
