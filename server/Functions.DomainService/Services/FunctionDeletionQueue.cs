using Blocks.Genesis;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    /// <summary>Hands an accepted delete to the Worker's background purge.</summary>
    public interface IFunctionDeletionQueue
    {
        /// <summary>
        /// Queues the purge of a tombstoned function. Never throws: the tombstone is the record of
        /// the delete, and the Worker's backstop sweep finds any the queue did not get.
        /// </summary>
        Task EnqueueAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IFunctionDeletionQueue"/>
    public sealed class FunctionDeletionQueue : IFunctionDeletionQueue
    {
        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionDeletionQueue> _logger;

        public FunctionDeletionQueue(ICacheClient cache, ILogger<FunctionDeletionQueue> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task EnqueueAsync(string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            try
            {
                await _cache.CacheDatabase().SetAddAsync(
                    FunctionWorkerQueueKeys.PendingDeletions,
                    FunctionWorkerQueueKeys.PendingDeletionMember(tenantId, functionId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not queue the purge of deleted function {FunctionId}; the deletion sweep will pick it up",
                    functionId);
            }
        }
    }
}
