using Blocks.Genesis;
using MongoDB.Driver;

namespace Common.InternalService.Storage
{
    public class ContentAccessRepository : IContentAccessRepository
    {
        internal const string PolicyCollectionName = "ContentAccessPolicies";
        internal const string AuditCollectionName = "ContentAuditLogs";

        private readonly IDbContextProvider _dbContextProvider;

        // Index creation is idempotent in Mongo, but issuing it on every call still costs a
        // round trip, so it is done once per repository instance per collection.
        private int _policyIndexesEnsured;
        private int _auditIndexesEnsured;

        public ContentAccessRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        /// <summary>
        /// Entries are active when they are not expired. An entry with no expiry never
        /// expires, so the filter has to admit null rather than compare against it.
        /// </summary>
        private static FilterDefinition<ContentAccessPolicy> Active(DateTime asOf)
        {
            var builder = Builders<ContentAccessPolicy>.Filter;
            return builder.Eq(p => p.ExpiresAt, null) | builder.Gt(p => p.ExpiresAt, asOf);
        }

        private IMongoCollection<ContentAccessPolicy> Policies =>
            _dbContextProvider.GetCollection<ContentAccessPolicy>(PolicyCollectionName);

        private IMongoCollection<ContentAuditLog> AuditLogs =>
            _dbContextProvider.GetCollection<ContentAuditLog>(AuditCollectionName);

        private async Task EnsurePolicyIndexesAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _policyIndexesEnsured, 1) == 1) return;

            var keys = Builders<ContentAccessPolicy>.IndexKeys;
            await Policies.Indexes.CreateManyAsync(
                new[]
                {
                    // Serves both the single-resource read and the batched ancestor and
                    // children reads, which are the hot paths in resolution.
                    new CreateIndexModel<ContentAccessPolicy>(
                        keys.Ascending(p => p.TenantId).Ascending(p => p.ResourceId)),
                    // Supports revoking every grant held by one principal.
                    new CreateIndexModel<ContentAccessPolicy>(
                        keys.Ascending(p => p.TenantId).Ascending(p => p.PrincipalType).Ascending(p => p.PrincipalId)),
                },
                cancellationToken);
        }

        private async Task EnsureAuditIndexesAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _auditIndexesEnsured, 1) == 1) return;

            var keys = Builders<ContentAuditLog>.IndexKeys;
            await AuditLogs.Indexes.CreateOneAsync(
                new CreateIndexModel<ContentAuditLog>(
                    keys.Ascending(a => a.TenantId).Ascending(a => a.ResourceId).Descending(a => a.CreatedDate)),
                cancellationToken: cancellationToken);
        }

        public async Task<List<ContentAccessPolicy>> GetByResourceAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resourceId)) return new List<ContentAccessPolicy>();

            await EnsurePolicyIndexesAsync(cancellationToken);

            var filter = Active(DateTime.UtcNow)
                         & Builders<ContentAccessPolicy>.Filter.Eq(p => p.ResourceId, resourceId);

            return await Policies.Find(filter).ToListAsync(cancellationToken);
        }

        public async Task<List<ContentAccessPolicy>> GetByResourcesAsync(IEnumerable<string> resourceIds, CancellationToken cancellationToken = default)
        {
            var ids = Distinct(resourceIds);
            if (ids.Count == 0) return new List<ContentAccessPolicy>();

            await EnsurePolicyIndexesAsync(cancellationToken);

            var filter = Active(DateTime.UtcNow)
                         & Builders<ContentAccessPolicy>.Filter.In(p => p.ResourceId, ids);

            return await Policies.Find(filter).ToListAsync(cancellationToken);
        }

        public async Task<HashSet<string>> GetResourceIdsWithPoliciesAsync(IEnumerable<string> resourceIds, CancellationToken cancellationToken = default)
        {
            var ids = Distinct(resourceIds);
            if (ids.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

            await EnsurePolicyIndexesAsync(cancellationToken);

            var filter = Active(DateTime.UtcNow)
                         & Builders<ContentAccessPolicy>.Filter.In(p => p.ResourceId, ids);

            var found = await Policies.DistinctAsync(p => p.ResourceId, filter, cancellationToken: cancellationToken);
            var result = await found.ToListAsync(cancellationToken);
            return new HashSet<string>(result, StringComparer.Ordinal);
        }

        public async Task GrantAsync(ContentAccessPolicy policy, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(policy);
            await EnsurePolicyIndexesAsync(cancellationToken);
            await Policies.InsertOneAsync(policy, cancellationToken: cancellationToken);
        }

        public async Task UpdateAsync(ContentAccessPolicy policy, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(policy);
            await EnsurePolicyIndexesAsync(cancellationToken);

            var filter = Builders<ContentAccessPolicy>.Filter.Eq(p => p.ItemId, policy.ItemId);

            await Policies.ReplaceOneAsync(filter, policy, cancellationToken: cancellationToken);
        }

        public async Task<bool> RevokeAsync(string policyItemId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(policyItemId)) return false;

            var filter = Builders<ContentAccessPolicy>.Filter.Eq(p => p.ItemId, policyItemId);

            var result = await Policies.DeleteOneAsync(filter, cancellationToken);
            return result.DeletedCount > 0;
        }

        public async Task<long> RevokeAllForResourceAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resourceId)) return 0;

            var filter = Builders<ContentAccessPolicy>.Filter.Eq(p => p.ResourceId, resourceId);

            var result = await Policies.DeleteManyAsync(filter, cancellationToken);
            return result.DeletedCount;
        }

        public async Task WriteAuditAsync(ContentAuditLog entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            await EnsureAuditIndexesAsync(cancellationToken);
            await AuditLogs.InsertOneAsync(entry, cancellationToken: cancellationToken);
        }

        public async Task<List<ContentAuditLog>> GetAuditForResourceAsync(string resourceId, int limit = 100, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resourceId)) return new List<ContentAuditLog>();

            await EnsureAuditIndexesAsync(cancellationToken);

            var filter = Builders<ContentAuditLog>.Filter.Eq(a => a.ResourceId, resourceId);

            return await AuditLogs.Find(filter)
                .SortByDescending(a => a.CreatedDate)
                .Limit(limit)
                .ToListAsync(cancellationToken);
        }

        private static List<string> Distinct(IEnumerable<string> ids) =>
            ids is null
                ? new List<string>()
                : ids.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
    }
}
