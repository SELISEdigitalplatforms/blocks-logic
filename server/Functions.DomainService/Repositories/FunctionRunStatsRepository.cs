using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    public interface IFunctionRunStatsRepository
    {
        /// <summary>
        /// Counts one started run. A single upsert with <c>$inc</c>/<c>$set</c> — no read first,
        /// so concurrent runs cannot lose each other's increments.
        /// </summary>
        Task RecordRunStartedAsync(
            string tenantId, string functionId, DateTime startedAt, CancellationToken cancellationToken = default);

        /// <summary>Counters for a page of functions, in one query rather than one per row.</summary>
        Task<IReadOnlyDictionary<string, FunctionRunStatsEntity>> GetManyAsync(
            string tenantId, IReadOnlyCollection<string> functionIds, CancellationToken cancellationToken = default);

        Task DeleteAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IFunctionRunStatsRepository"/>
    public class FunctionRunStatsRepository : IFunctionRunStatsRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<FunctionRunStatsRepository> _logger;

        public FunctionRunStatsRepository(
            IDbContextProvider dbContextProvider, ILogger<FunctionRunStatsRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoCollection<FunctionRunStatsEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionRunStatsEntity>(
                tenantId, FunctionsConstants.FunctionRunStatsCollection);

        public async Task RecordRunStartedAsync(
            string tenantId, string functionId, DateTime startedAt, CancellationToken cancellationToken = default)
        {
            try
            {
                // ItemId is the function id, so there is exactly one counter document per
                // function and the upsert needs no separate create path.
                var update = Builders<FunctionRunStatsEntity>.Update
                    .Inc(s => s.TotalRuns, 1)
                    .Set(s => s.LastRunAt, startedAt)
                    .SetOnInsert(s => s.ItemId, functionId)
                    .SetOnInsert(s => s.CreatedDate, DateTime.UtcNow);

                await Collection(tenantId).UpdateOneAsync(
                    s => s.ItemId == functionId,
                    update,
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // A counter is not worth failing an invocation over.
                _logger.LogError(ex, "Failed to count a run for function {FunctionId}", functionId);
            }
        }

        public async Task<IReadOnlyDictionary<string, FunctionRunStatsEntity>> GetManyAsync(
            string tenantId, IReadOnlyCollection<string> functionIds, CancellationToken cancellationToken = default)
        {
            if (functionIds.Count == 0) return new Dictionary<string, FunctionRunStatsEntity>(StringComparer.Ordinal);

            try
            {
                var documents = await Collection(tenantId)
                    .Find(Builders<FunctionRunStatsEntity>.Filter.In(s => s.ItemId, functionIds))
                    .ToListAsync(cancellationToken);

                return documents.ToDictionary(d => d.ItemId, d => d, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                // The list is still worth rendering without its counters.
                _logger.LogError(ex, "Failed to read run counters for tenant {TenantId}", tenantId);
                return new Dictionary<string, FunctionRunStatsEntity>(StringComparer.Ordinal);
            }
        }

        public async Task DeleteAsync(string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            try
            {
                await Collection(tenantId).DeleteOneAsync(s => s.ItemId == functionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete run counters for function {FunctionId}", functionId);
            }
        }
    }
}
