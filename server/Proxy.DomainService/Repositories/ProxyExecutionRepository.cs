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
    /// The <c>{ ProxyId: 1, StartedAtUtc: -1, ItemId: -1 }</c>, <c>{ TenantId: 1, StartedAtUtc: -1 }</c> and
    /// <c>{ ProxyId: 1, StatusCode: 1, StartedAtUtc: -1, ItemId: -1 }</c> indexes are created once per tenant,
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
        /// <para>
        /// The <c>_item</c> suffixed names supersede the earlier <c>ix_proxyexec_proxy_started</c> and
        /// <c>ix_proxyexec_proxy_status_started</c>, which are strict prefixes of them and so carry write cost
        /// for no read benefit. They are intentionally NOT dropped here — dropping indexes as a side effect of
        /// a write path is not something this should decide on its own. Drop them per tenant database when
        /// convenient:
        /// <c>db.ProxyExecutions.dropIndex("ix_proxyexec_proxy_started")</c> and
        /// <c>db.ProxyExecutions.dropIndex("ix_proxyexec_proxy_status_started")</c>.
        /// </para>
        /// </summary>
        private async Task EnsureIndexesAsync(string tenantId, IMongoCollection<ProxyExecutionEntity> collection)
        {
            if (!_indexedTenants.TryAdd(tenantId, 0))
            {
                return;
            }

            try
            {
                // Every read sorts by NewestFirst — StartedAtUtc desc THEN ItemId desc. The tiebreaker has to
                // be in the index too: an index that only covers the first sort key still forces Mongo into a
                // blocking in-memory SORT over the whole 24h match set to resolve ties, which is exactly the
                // query that grows without bound on a busy proxy.
                var byProxy = Builders<ProxyExecutionEntity>.IndexKeys
                    .Ascending(e => e.ProxyId)
                    .Descending(e => e.StartedAtUtc)
                    .Descending(e => e.ItemId);
                var byTenant = Builders<ProxyExecutionEntity>.IndexKeys
                    .Ascending(e => e.TenantId)
                    .Descending(e => e.StartedAtUtc);

                // NOTE: this one still cannot serve the sort. `statusClass` is applied as a RANGE on
                // StatusCode (>= 400 && <= 499), and a range on a middle key voids the ordering guarantee for
                // every key after it. It stays because it still narrows the candidate set cheaply; removing
                // the blocking sort for filtered views needs a denormalised equality field (a stored
                // "2xx"/"4xx"/"5xx" StatusClass), which is a schema change plus a backfill.
                var byProxyStatus = Builders<ProxyExecutionEntity>.IndexKeys
                    .Ascending(e => e.ProxyId)
                    .Ascending(e => e.StatusCode)
                    .Descending(e => e.StartedAtUtc)
                    .Descending(e => e.ItemId);

                await collection.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<ProxyExecutionEntity>(byProxy, new CreateIndexOptions { Name = "ix_proxyexec_proxy_started_item" }),
                    new CreateIndexModel<ProxyExecutionEntity>(byTenant, new CreateIndexOptions { Name = "ix_proxyexec_tenant_started" }),
                    new CreateIndexModel<ProxyExecutionEntity>(byProxyStatus, new CreateIndexOptions { Name = "ix_proxyexec_proxy_status_started_item" }),
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
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc, DateTime asOfUtc,
            int pageSize, int pageNumber)
        {
            return await GetCollection(tenantId)
                .Find(WindowFilter(tenantId, proxyId, statusClass, sinceUtc, asOfUtc))
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

        public async Task<long> CountAsync(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc, DateTime asOfUtc)
        {
            return await GetCollection(tenantId)
                .CountDocumentsAsync(WindowFilter(tenantId, proxyId, statusClass, sinceUtc, asOfUtc));
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
        /// <summary>
        /// The 24h window for one proxy, narrowed to a status class. <paramref name="asOfUtc"/> bounds the
        /// window above so a paging session sees a fixed set of rows; the Live tail passes <c>null</c>, since
        /// its whole job is to pick up rows newer than everything it has seen.
        /// </summary>
        private static FilterDefinition<ProxyExecutionEntity> WindowFilter(
            string tenantId, string proxyId, ProxyStatusClass statusClass, DateTime sinceUtc,
            DateTime? asOfUtc = null)
        {
            var builder = Builders<ProxyExecutionEntity>.Filter;
            var filter = builder.Eq(e => e.TenantId, tenantId)
                         & builder.Eq(e => e.ProxyId, proxyId)
                         & builder.Gte(e => e.StartedAtUtc, sinceUtc);

            if (asOfUtc is { } asOf)
            {
                filter &= builder.Lte(e => e.StartedAtUtc, asOf);
            }

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
