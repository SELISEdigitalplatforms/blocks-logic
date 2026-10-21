using Blocks.Genesis;
using DataAccess.InternalService;
using Functions.DomainService.Utils;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Repositories
{
    /// <inheritdoc cref="IFunctionBuildRepository"/>
    public class FunctionBuildRepository : IFunctionBuildRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly HashSet<string> _indexedTenants = [];
        private readonly Lock _indexGate = new();

        private readonly ILogicDataAccess _dataAccess;

        public FunctionBuildRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }

        private Task<ReadScope<FunctionBuildEntity>> ReadAsync(string tenantId, FilterDefinition<FunctionBuildEntity> filter, string callSite) =>
            _dataAccess.ReadAsync(LogicSchemas.FunctionBuildEntity, filter, $"FunctionBuildRepository.{callSite}", tenantId);

        private Task<FilterDefinition<FunctionBuildEntity>?> EditAsync(string tenantId, FilterDefinition<FunctionBuildEntity> filter, string callSite) =>
            _dataAccess.EditFilterAsync(LogicSchemas.FunctionBuildEntity, filter, $"FunctionBuildRepository.{callSite}", tenantId);

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
            var scope = await ReadAsync(tenantId, Builders<FunctionBuildEntity>.Filter.Where(b => b.ItemId == buildId), "GetById");
            return scope.Granted ? await scope.Apply(Collection(tenantId)).FirstOrDefaultAsync(cancellationToken) : null;
        }

        public async Task<FunctionBuildEntity?> GetSucceededBySourceHashAsync(
            string tenantId, string functionId, string sourceHash, CancellationToken cancellationToken = default)
        {
            await EnsureIndexesAsync(tenantId, cancellationToken);

            var filter = Builders<FunctionBuildEntity>.Filter.Eq(b => b.FunctionId, functionId)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.SourceHash, sourceHash)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.Status, BuildStatus.Succeeded)
                       & Builders<FunctionBuildEntity>.Filter.Ne(b => b.Ephemeral, true);

            var scope = await ReadAsync(tenantId, filter, "GetSucceededBySourceHash");
            if (!scope.Granted) return null;
            return await scope.Apply(Collection(tenantId))
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
                       & Builders<FunctionBuildEntity>.Filter.In(b => b.Status, inProgress)
                       & Builders<FunctionBuildEntity>.Filter.Ne(b => b.Ephemeral, true);

            var scope = await ReadAsync(tenantId, filter, "GetInProgressBySourceHash");
            if (!scope.Granted) return null;
            return await scope.Apply(Collection(tenantId))
                .SortByDescending(b => b.CreatedDate)
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<FunctionBuildEntity>> GetAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return [];

            var scope = await ReadAsync(tenantId, Builders<FunctionBuildEntity>.Filter.Where(b => b.FunctionId == functionId), "GetAllForFunction");
            if (!scope.Granted) return [];
            return await scope.Apply(Collection(tenantId))
                .SortByDescending(b => b.CreatedDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return 0;

            var deleteFilter = await _dataAccess.DeleteFilterAsync(LogicSchemas.FunctionBuildEntity,
                Builders<FunctionBuildEntity>.Filter.Where(b => b.FunctionId == functionId), "FunctionBuildRepository.DeleteAllForFunction", tenantId);
            if (deleteFilter is null) return 0;
            var result = await Collection(tenantId).DeleteManyAsync(deleteFilter, cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> InvalidateByImageDigestAsync(
            string tenantId, string imageDigest, string reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imageDigest)) return 0;

            var filter = await EditAsync(tenantId, Builders<FunctionBuildEntity>.Filter.Eq(b => b.ImageDigest, imageDigest)
                       & Builders<FunctionBuildEntity>.Filter.Eq(b => b.Status, BuildStatus.Succeeded), "InvalidateByImageDigest");
            if (filter is null) return 0;

            var update = Builders<FunctionBuildEntity>.Update
                .Set(b => b.Status, BuildStatus.Failed)
                .Set(b => b.ErrorMessage, reason)
                .Set(b => b.LastUpdatedDate, DateTime.UtcNow);

            var result = await Collection(tenantId).UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
            return result.ModifiedCount;
        }

        public async Task CreateAsync(string tenantId, FunctionBuildEntity build, CancellationToken cancellationToken = default)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.FunctionBuildEntity, "FunctionBuildRepository.Create", tenantId))
                throw new UnauthorizedAccessException("Function build create is not allowed by the data access policy.");
            await _dataAccess.PrepareWriteAsync(LogicSchemas.FunctionBuildEntity, build, isInsert: true, "FunctionBuildRepository.Create", tenantId);
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
            var filter = await EditAsync(tenantId, Builders<FunctionBuildEntity>.Filter.And(
                Builders<FunctionBuildEntity>.Filter.Eq(b => b.ItemId, buildId),
                Builders<FunctionBuildEntity>.Filter.Nin(b => b.Status, TerminalStatuses)), "FailIfNotTerminal");
            if (filter is null) return false;

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
            string? artifactSha256,
            string? baseImage,
            string? packages,
            string? log,
            string? errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default)
        {
            var update = Builders<FunctionBuildEntity>.Update
                .Set(b => b.Status, status)
                .Set(b => b.ImageDigest, imageDigest)
                .Set(b => b.ArtifactSha256, artifactSha256)
                .Set(b => b.BaseImage, string.IsNullOrWhiteSpace(baseImage) ? null : baseImage)
                .Set(b => b.Packages, packages)
                .Set(b => b.Log, log)
                .Set(b => b.ErrorMessage, errorMessage)
                .Set(b => b.CompletedAt, completedAt)
                .Set(b => b.LastUpdatedDate, DateTime.UtcNow);

            var filter = await EditAsync(tenantId, Builders<FunctionBuildEntity>.Filter.Where(b => b.ItemId == buildId), "ApplyResult");
            if (filter is null) return false;
            var result = await Collection(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
            return result.MatchedCount > 0;
        }
    }
}
