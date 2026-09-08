using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IProxyExecutionRepository"/>. Collections are resolved per tenant by
    /// <see cref="IDbContextProvider"/>, exactly as <see cref="ProxyRepository"/> and the Workflow domain do.
    /// The <c>{ ProxyId: 1, StartedAtUtc: -1 }</c>, <c>{ TenantId: 1, StartedAtUtc: -1 }</c> and
    /// <c>{ ProxyId: 1, StatusCode: 1, StartedAtUtc: -1 }</c> indexes are created once per tenant,
    /// best-effort, on first write. Phase 3's read methods rely on them but never create them.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class ProxyExecutionRepository : IProxyExecutionRepository
    {
        private const string CollectionName = "ProxyExecutions";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly ConcurrentDictionary<string, byte> _indexedTenants = new(StringComparer.Ordinal);

        public ProxyExecutionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<ProxyExecutionEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<ProxyExecutionEntity>(tenantId, CollectionName);
        }

        /// <summary>
        /// Creates the recommended indexes once per tenant database. Failures (for example an existing
        /// incompatible index) are swallowed so they never block a write, mirroring the other domains.
        /// </summary>
        private async Task EnsureIndexesAsync(string tenantId, IMongoCollection<ProxyExecutionEntity> collection)
        {
            if (!_indexedTenants.TryAdd(tenantId, 0))
            {
                return;
            }

            try
            {
                var byProxy = Builders<ProxyExecutionEntity>.IndexKeys
                    .Ascending(e => e.ProxyId)
                    .Descending(e => e.StartedAtUtc);
                var byTenant = Builders<ProxyExecutionEntity>.IndexKeys
                    .Ascending(e => e.TenantId)
                    .Descending(e => e.StartedAtUtc);
                var byProxyStatus = Builders<ProxyExecutionEntity>.IndexKeys
                    .Ascending(e => e.ProxyId)
                    .Ascending(e => e.StatusCode)
                    .Descending(e => e.StartedAtUtc);

                await collection.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<ProxyExecutionEntity>(byProxy, new CreateIndexOptions { Name = "ix_proxyexec_proxy_started" }),
                    new CreateIndexModel<ProxyExecutionEntity>(byTenant, new CreateIndexOptions { Name = "ix_proxyexec_tenant_started" }),
                    new CreateIndexModel<ProxyExecutionEntity>(byProxyStatus, new CreateIndexOptions { Name = "ix_proxyexec_proxy_status_started" }),
                });
            }
            catch (MongoCommandException)
            {
                _indexedTenants.TryRemove(tenantId, out _);
            }
        }

        public async Task InsertAsync(ProxyExecutionEntity execution)
        {
            if (string.IsNullOrEmpty(execution.TenantId))
            {
                throw new InvalidOperationException("TenantId is required for a proxy execution row.");
            }

            var collection = GetCollection(execution.TenantId);
            await EnsureIndexesAsync(execution.TenantId, collection);
            await collection.InsertOneAsync(execution);
        }

        // ---------- Phase 3 reads ----------

        public async Task<List<ProxyExecutionEntity>> GetPageAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc, int pageSize, int pageNumber)
        {
            return await GetCollection(tenantId)
                .Find(WindowFilter(tenantId, proxyId, statusClass, sinceUtc))
                .Sort(NewestFirst)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<List<ProxyExecutionEntity>> GetNewerThanAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc,
            DateTime afterStartedAtUtc, string afterItemId, int pageSize)
        {
            var builder = Builders<ProxyExecutionEntity>.Filter;
            var strictlyNewer = builder.Or(
                builder.Gt(e => e.StartedAtUtc, afterStartedAtUtc),
                builder.And(
                    builder.Eq(e => e.StartedAtUtc, afterStartedAtUtc),
                    builder.Gt(e => e.ItemId, afterItemId)));

            return await GetCollection(tenantId)
                .Find(WindowFilter(tenantId, proxyId, statusClass, sinceUtc) & strictlyNewer)
                .Sort(NewestFirst)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<long> CountAsync(string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc)
        {
            return await GetCollection(tenantId)
                .CountDocumentsAsync(WindowFilter(tenantId, proxyId, statusClass, sinceUtc));
        }

        public async Task<ProxyExecutionEntity?> GetByIdAsync(string tenantId, string proxyId, string itemId)
        {
            var builder = Builders<ProxyExecutionEntity>.Filter;
            var filter = builder.Eq(e => e.TenantId, tenantId)
                         & builder.Eq(e => e.ProxyId, proxyId)
                         & builder.Eq(e => e.ItemId, itemId);
            return await GetCollection(tenantId).Find(filter).FirstOrDefaultAsync();
        }

        public async Task<ProxyExecutionEntity?> FindByItemIdAsync(string tenantId, string itemId)
        {
            var builder = Builders<ProxyExecutionEntity>.Filter;
            var filter = builder.Eq(e => e.TenantId, tenantId) & builder.Eq(e => e.ItemId, itemId);
            return await GetCollection(tenantId).Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> AnyForProxyAsync(string tenantId, string proxyId)
        {
            var builder = Builders<ProxyExecutionEntity>.Filter;
            var filter = builder.Eq(e => e.TenantId, tenantId) & builder.Eq(e => e.ProxyId, proxyId);
            return await GetCollection(tenantId).Find(filter).Limit(1).AnyAsync();
        }

        public async Task<ProxyExecutionStats> GetStatsAsync(string tenantId, string proxyId, DateTime sinceUtc)
        {
            var doc = await GetCollection(tenantId)
                .Aggregate()
                .Match(WindowFilter(tenantId, proxyId, ProxyStatusClass.All, sinceUtc))
                .Group(new BsonDocument
                {
                    { "_id", BsonNull.Value },
                    { "count", new BsonDocument("$sum", 1) },
                    { "avgLatency", new BsonDocument("$avg", "$LatencyMs") },
                    {
                        "errorCount", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                        {
                            new BsonDocument("$gte", new BsonArray { "$StatusCode", 400 }),
                            1,
                            0,
                        }))
                    },
                    { "lastCall", new BsonDocument("$max", "$StartedAtUtc") },
                })
                .FirstOrDefaultAsync();

            if (doc is null)
            {
                return ProxyExecutionStats.Empty;
            }

            var avg = doc.TryGetValue("avgLatency", out var avgValue) && !avgValue.IsBsonNull ? avgValue.ToDouble() : 0d;
            DateTime? lastCall = doc.TryGetValue("lastCall", out var lastValue) && !lastValue.IsBsonNull
                ? lastValue.ToUniversalTime()
                : null;

            return new ProxyExecutionStats(doc["count"].ToInt64(), avg, doc["errorCount"].ToInt64(), lastCall);
        }

        public async Task<IReadOnlyDictionary<string, long>> CountByProxyAsync(
            string tenantId, IReadOnlyCollection<string> proxyIds, DateTime sinceUtc)
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            if (proxyIds.Count == 0)
            {
                return map;
            }

            var builder = Builders<ProxyExecutionEntity>.Filter;
            var filter = builder.Eq(e => e.TenantId, tenantId)
                         & builder.In(e => e.ProxyId, proxyIds)
                         & builder.Gte(e => e.StartedAtUtc, sinceUtc);

            var docs = await GetCollection(tenantId)
                .Aggregate()
                .Match(filter)
                .Group(new BsonDocument
                {
                    { "_id", "$ProxyId" },
                    { "count", new BsonDocument("$sum", 1) },
                })
                .ToListAsync();

            foreach (var doc in docs)
            {
                map[doc["_id"].AsString] = doc["count"].ToInt64();
            }

            return map;
        }

        public async Task<List<ProxyExecutionEntity>> GetForExportAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc, int limit)
        {
            return await GetCollection(tenantId)
                .Find(WindowFilter(tenantId, proxyId, statusClass, sinceUtc))
                .Sort(NewestFirst)
                .Limit(limit)
                .ToListAsync();
        }

        private static SortDefinition<ProxyExecutionEntity> NewestFirst =>
            Builders<ProxyExecutionEntity>.Sort
                .Descending(e => e.StartedAtUtc)
                .Descending(e => e.ItemId);

        /// <summary>
        /// <c>TenantId</c> + <c>ProxyId</c> + <c>StartedAtUtc &gt;= sinceUtc</c> + the status-class band. The
        /// <c>TenantId</c> clause is defence-in-depth on top of the per-tenant collection.
        /// </summary>
        private static FilterDefinition<ProxyExecutionEntity> WindowFilter(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc)
        {
            var builder = Builders<ProxyExecutionEntity>.Filter;
            var filter = builder.Eq(e => e.TenantId, tenantId)
                         & builder.Eq(e => e.ProxyId, proxyId)
                         & builder.Gte(e => e.StartedAtUtc, sinceUtc);

            return statusClass switch
            {
                ProxyStatusClass.TwoXx => filter & builder.Gte(e => e.StatusCode, 200) & builder.Lte(e => e.StatusCode, 299),
                ProxyStatusClass.FourXx => filter & builder.Gte(e => e.StatusCode, 400) & builder.Lte(e => e.StatusCode, 499),
                ProxyStatusClass.FiveXx => filter & builder.Gte(e => e.StatusCode, 500) & builder.Lte(e => e.StatusCode, 599),
                _ => filter,
            };
        }
    }
}
