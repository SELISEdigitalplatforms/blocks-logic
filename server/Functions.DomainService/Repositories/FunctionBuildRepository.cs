using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Utils;
using MongoDB.Driver;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionBuildRepository"/>
    public class FunctionBuildRepository : IFunctionBuildRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        public FunctionBuildRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<FunctionBuildEntity> Collection(string tenantId)
            => _dbContextProvider.GetCollection<FunctionBuildEntity>(tenantId, FunctionsConstants.FunctionBuildsCollection);

        private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId)) return;
            }

            var keys = Builders<FunctionBuildEntity>.IndexKeys;
            await Collection(tenantId).Indexes.CreateManyAsync(
                [
                    // The build cache lookup: newest build for a given source hash first, so
                    // a re-triggered Test after a failed build finds the failure, not a stale
                    // success from before an edit that was later reverted.
                    new CreateIndexModel<FunctionBuildEntity>(
                        keys.Ascending(b => b.FunctionId).Ascending(b => b.SourceHash).Descending(b => b.CreatedDate)),
                    new CreateIndexModel<FunctionBuildEntity>(
                        keys.Ascending(b => b.CreatedDate),
                        new CreateIndexOptions { ExpireAfter = FunctionsConstants.BuildRetention }),
                ],
                cancellationToken);
        }

        public async Task<FunctionBuildEntity?> GetByIdAsync(string tenantId, string buildId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(buildId)) return null;
            return await Collection(tenantId).Find(b => b.ItemId == buildId).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<FunctionBuildEntity?> GetSucceededBySourceHashAsync(
            string tenantId, string functionId, string sourceHash, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var filter = Builders<FunctionBuildEntity>.Filter.Eq(b => b.FunctionId, functionId)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.SourceHash, sourceHash)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.Status, BuildStatus.Succeeded);

            return await Collection(tenantId).Find(filter)
                .SortByDescending(b => b.CreatedDate)
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<FunctionBuildEntity?> GetInProgressBySourceHashAsync(
            string tenantId, string functionId, string sourceHash, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var inProgress = new[] { BuildStatus.Queued, BuildStatus.Building };
            var filter = Builders<FunctionBuildEntity>.Filter.Eq(b => b.FunctionId, functionId)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.SourceHash, sourceHash)
                       & Builders<FunctionBuildEntity>.Filter.In(b => b.Status, inProgress);

            return await Collection(tenantId).Find(filter)
                .SortByDescending(b => b.CreatedDate)
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<FunctionBuildEntity>> GetAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return [];

            return await Collection(tenantId)
                .Find(b => b.FunctionId == functionId)
                .SortByDescending(b => b.CreatedDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return 0;

            var result = await Collection(tenantId).DeleteManyAsync(b => b.FunctionId == functionId, cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> InvalidateByImageDigestAsync(
            string tenantId, string imageDigest, string reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imageDigest)) return 0;

            var filter = Builders<FunctionBuildEntity>.Filter.Eq(b => b.ImageDigest, imageDigest)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.Status, BuildStatus.Succeeded);

            var update = Builders<FunctionBuildEntity>.Update
                .Set(b => b.Status, BuildStatus.Failed)
                .Set(b => b.ErrorMessage, reason)
                .Set(b => b.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount;
        }

        public async Task CreateAsync(string tenantId, FunctionBuildEntity build, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);
            await Collection(tenantId).InsertOneAsync(build, cancellationToken: cancellationToken);
        }

        /// <summary>The build statuses past which a dead letter must not write.</summary>
        internal static readonly BuildStatus[] TerminalStatuses = [BuildStatus.Succeeded, BuildStatus.Failed];

        public async Task<bool> FailIfNotTerminalAsync(
            string tenantId,
            string buildId,
            string errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default)
        {
            var filter = Builders<FunctionBuildEntity>.Filter.And(
                Builders<FunctionBuildEntity>.Filter.Eq(b => b.ItemId, buildId),
                Builders<FunctionBuildEntity>.Filter.Nin(b => b.Status, TerminalStatuses));

            var update = Builders<FunctionBuildEntity>.Update
                .Set(b => b.Status, BuildStatus.Failed)
                .Set(b => b.ErrorMessage, errorMessage)
                .Set(b => b.CompletedAt, completedAt)
                .Set(b => b.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(
                filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ApplyResultAsync(
            string tenantId,
            string buildId,
            BuildStatus status,
            string? imageDigest,
            string? packages,
            string? log,
            string? errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionBuildEntity>.Update
                .Set(b => b.Status, status)
                .Set(b => b.ImageDigest, imageDigest)
                .Set(b => b.Packages, packages)
                .Set(b => b.Log, log)
                .Set(b => b.ErrorMessage, errorMessage)
                .Set(b => b.CompletedAt, completedAt)
                .Set(b => b.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateOneAsync(
                b => b.ItemId == buildId, update, cancellationToken: cancellationToken);
            return result.MatchedCount > 0;
        }
    }
}
