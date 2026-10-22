using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Blocks.Genesis;
using DataAccess.InternalService;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace Proxy.DomainService.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IProxyRepository"/>. Collections are resolved per tenant by
    /// <see cref="IDbContextProvider"/>, exactly as the Workflow domain does. The unique index on
    /// <c>Slug</c> is created lazily on first use per tenant, best-effort.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class ProxyRepository : IProxyRepository
    {
        private const string CollectionName = "Proxies";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<ProxyRepository> _logger;
        private readonly ConcurrentDictionary<string, byte> _indexedTenants = new(StringComparer.Ordinal);

        private readonly ILogicDataAccess _dataAccess;

        public ProxyRepository(IDbContextProvider dbContextProvider, ILogger<ProxyRepository> logger, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
            _dataAccess = dataAccess;
        }

        private Task<ReadScope<ProxyDetailEntity>> ReadAsync(string tenantId, FilterDefinition<ProxyDetailEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.ProxyDetailEntity, filter, $"ProxyRepository.{callSite}", tenantId, useProjection);

        private Task<FilterDefinition<ProxyDetailEntity>?> EditAsync(string tenantId, FilterDefinition<ProxyDetailEntity> filter, string callSite) =>
            _dataAccess.EditFilterAsync(LogicSchemas.ProxyDetailEntity, filter, $"ProxyRepository.{callSite}", tenantId);

        private IMongoCollection<ProxyDetailEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<ProxyDetailEntity>(tenantId, CollectionName);
        }

        /// <summary>
        /// Creates the unique <c>{ Slug: 1 }</c> index once per tenant database. Failures (for example an
        /// existing incompatible index) are swallowed so they never block a write, mirroring the other domains.
        /// </summary>
        private async Task EnsureIndexesAsync(string tenantId, IMongoCollection<ProxyDetailEntity> collection)
        {
            if (!_indexedTenants.TryAdd(tenantId, 0))
            {
                return;
            }

            try
            {
                var keys = Builders<ProxyDetailEntity>.IndexKeys.Ascending(p => p.Slug);
                await collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<ProxyDetailEntity>(
                        keys, new CreateIndexOptions { Unique = true, Name = "ux_proxy_slug" }));
            }
            catch (MongoCommandException ex)
            {
                // Swallowed so an index problem never blocks a write, but NOT silently: while this keeps
                // failing, slug uniqueness rests on the check-then-write in ProxyService alone and the
                // concurrent-create race is open again. That has to be diagnosable.
                _logger.LogWarning(
                    ex,
                    "Proxy repository: could not create the unique slug index 'ux_proxy_slug' for tenant {TenantId}. "
                    + "Slug uniqueness is not enforced by the database until this succeeds.",
                    tenantId);
                _indexedTenants.TryRemove(tenantId, out _);
            }
        }

        private static FilterDefinition<ProxyDetailEntity> BuildListFilter(string? search, bool? isActive)
        {
            var builder = Builders<ProxyDetailEntity>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
                filter &= builder.Or(builder.Regex(p => p.Name, pattern), builder.Regex(p => p.Slug, pattern));
            }

            if (isActive.HasValue)
            {
                filter &= builder.Eq(p => p.Enabled, isActive.Value);
            }

            return filter;
        }

        private static ProxyDetailEntity? NormalizeTenant(ProxyDetailEntity? proxy, string tenantId)
        {
            if (proxy != null && !string.Equals(proxy.TenantId, tenantId, StringComparison.Ordinal))
            {
                proxy.TenantId = tenantId;
            }

            return proxy;
        }

        public async Task<ProxyDetailEntity?> GetAsync(string tenantId, string itemId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.ItemId, itemId);
            // The entity feeds SaveConfig (writes every field back): read it whole, no projection.
            var scope = await ReadAsync(tenantId, filter, "Get", useProjection: false);
            return scope.Granted ? NormalizeTenant(await scope.Apply(collection).FirstOrDefaultAsync(), tenantId) : null;
        }

        public async Task<ProxyDetailEntity?> GetForViewAsync(string tenantId, string itemId)
        {
            // Masked: View column (property) rules clear what the caller may not see. Never save it back.
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.ItemId, itemId);
            var scope = await ReadAsync(tenantId, filter, "GetForView");
            return scope.Granted ? scope.ForView(NormalizeTenant(await scope.Apply(collection).FirstOrDefaultAsync(), tenantId)) : null;
        }

        public async Task<ProxyDetailEntity?> GetBySlugAsync(string tenantId, string slug)
        {
            var collection = GetCollection(tenantId);
            await EnsureIndexesAsync(tenantId, collection);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.Slug, slug);
            // Gateway hot path (anonymous callers: skip path, no extra reads). Whole entity, no projection.
            var scope = await ReadAsync(tenantId, filter, "GetBySlug", useProjection: false);
            return scope.Granted ? NormalizeTenant(await scope.Apply(collection).FirstOrDefaultAsync(), tenantId) : null;
        }

        public async Task<List<ProxyDetailEntity>> GetAllAsync(
            string tenantId, string? search, bool? isActive, int pageSize, int pageNumber)
        {
            var collection = GetCollection(tenantId);
            var scope = await ReadAsync(tenantId, BuildListFilter(search, isActive), "GetAll", useProjection: false);
            if (!scope.Granted) return [];
            var proxies = await scope.Apply(collection)
                .SortByDescending(p => p.CreatedDate)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            foreach (var proxy in proxies)
            {
                NormalizeTenant(proxy, tenantId);
            }

            return scope.ForView(proxies);
        }

        public async Task<long> CountAsync(string tenantId, string? search, bool? isActive)
        {
            var collection = GetCollection(tenantId);
            var scope = await ReadAsync(tenantId, BuildListFilter(search, isActive), "Count");
            return scope.Granted ? await collection.CountDocumentsAsync(scope.Filter) : 0;
        }

        public async Task InsertAsync(ProxyDetailEntity proxy)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.ProxyDetailEntity, "ProxyRepository.Insert", proxy.TenantId))
                throw new UnauthorizedAccessException("Proxy create is not allowed by the data access policy.");
            await _dataAccess.PrepareWriteAsync(LogicSchemas.ProxyDetailEntity, proxy, isInsert: true, "ProxyRepository.Insert", proxy.TenantId);
            var collection = GetCollection(proxy.TenantId);
            await EnsureIndexesAsync(proxy.TenantId, collection);
            await collection.InsertOneAsync(proxy);
        }

        /// <summary>
        /// Fields a config write never touches: the id, and <c>Stats</c>, which the stats flush <c>$inc</c>s at the
        /// same time. A whole-document replace used to write back the stats it had read, losing counts (PX-16).
        /// </summary>
        private static readonly HashSet<string> NotConfigFields = new(StringComparer.Ordinal)
        {
            "_id",
            nameof(ProxyDetailEntity.Stats),
        };

        public Task<bool> SaveConfigAsync(ProxyDetailEntity proxy, int expectedVersion) =>
            SaveAsync(proxy, expectedVersion, keepHidden: false);

        public Task<bool> SaveEditedConfigAsync(ProxyDetailEntity proxy, int expectedVersion) =>
            SaveAsync(proxy, expectedVersion, keepHidden: true);

        private async Task<bool> SaveAsync(ProxyDetailEntity proxy, int expectedVersion, bool keepHidden)
        {
            if (string.IsNullOrWhiteSpace(proxy.TenantId))
            {
                throw new InvalidOperationException("TenantId is required for a proxy.");
            }

            var collection = GetCollection(proxy.TenantId);
            var builder = Builders<ProxyDetailEntity>.Filter;
            var filter = await EditAsync(proxy.TenantId, builder.Eq(p => p.ItemId, proxy.ItemId) & builder.Eq(p => p.CurrentVersion, expectedVersion), "SaveConfig");
            if (filter is null) return false;

            // $set each mapped field from the entity's own BSON, so a new config field is saved without a change here.
            var sets = proxy.ToBsonDocument().Elements
                .Where(e => !NotConfigFields.Contains(e.Name))
                .Select(e => Builders<ProxyDetailEntity>.Update.Set(e.Name, e.Value));
            var update = Builders<ProxyDetailEntity>.Update.Combine(sets);
            if (keepHidden)
            {
                var kept = await _dataAccess.KeepHiddenAsync(LogicSchemas.ProxyDetailEntity, update, collection, filter, "ProxyRepository.SaveConfig", proxy.TenantId);
                if (kept is null) return true;
                update = kept;
            }
            await _dataAccess.CheckUpdateAsync(LogicSchemas.ProxyDetailEntity, update, "ProxyRepository.SaveConfig", proxy.TenantId);
            var result = await collection.UpdateOneAsync(filter, update);
            return result.MatchedCount == 1;
        }

        public async Task<bool> DeleteAsync(string tenantId, string itemId, int expectedVersion)
        {
            var collection = GetCollection(tenantId);
            var builder = Builders<ProxyDetailEntity>.Filter;
            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.ProxyDetailEntity,
                builder.Eq(p => p.ItemId, itemId) & builder.Eq(p => p.CurrentVersion, expectedVersion), "ProxyRepository.Delete", tenantId);
            if (filter is null) return false;
            var result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount == 1;
        }

        public async Task ApplyStatsDeltasAsync(
            string tenantId, IReadOnlyList<ProxyStatsDelta> deltas, CancellationToken cancellationToken = default)
        {
            if (deltas.Count == 0)
            {
                return;
            }

            var collection = GetCollection(tenantId);

            // Computed once per batch, not per delta: every document in this flush prunes the same two hours.
            var expiredStamps = ProxyStatsWindow.ExpiredStamps(DateTime.UtcNow).ToList();

            // One EDIT resolve with an empty base filter, ANDed into every model. The flush runs on a timer
            // (no user context): the helper takes the no-context skip path and returns the base filter unchanged.
            var policy = await EditAsync(tenantId, Builders<ProxyDetailEntity>.Filter.Empty, "ApplyStatsDeltas");
            if (policy is null) return;

            var models = new List<WriteModel<ProxyDetailEntity>>(deltas.Count);
            foreach (var delta in deltas)
            {
                var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.ItemId, delta.ProxyId) & policy;

                // Dotted paths into the bucket map: $inc creates the hour's sub-document and its fields when
                // they are missing, so the first call of an hour needs no separate insert and no upsert race.
                var bucket = $"{nameof(ProxyDetailEntity.Stats)}.{nameof(ProxyStats.Buckets)}.{delta.Stamp}";
                var update = Builders<ProxyDetailEntity>.Update
                    .Inc($"{bucket}.{nameof(ProxyStatsBucket.Calls)}", delta.Calls)
                    .Inc($"{bucket}.{nameof(ProxyStatsBucket.Errors)}", delta.Errors)
                    .Inc($"{bucket}.{nameof(ProxyStatsBucket.LatencyMsTotal)}", delta.LatencyMsTotal)
                    // All-time counters, never $unset: back the Overview tile's all-time avgLatencyMs/errorRate.
                    .Inc($"{nameof(ProxyDetailEntity.Stats)}.{nameof(ProxyStats.TotalCalls)}", delta.Calls)
                    .Inc($"{nameof(ProxyDetailEntity.Stats)}.{nameof(ProxyStats.TotalErrors)}", delta.Errors)
                    .Inc(
                        $"{nameof(ProxyDetailEntity.Stats)}.{nameof(ProxyStats.TotalLatencyMsTotal)}",
                        delta.LatencyMsTotal)
                    .Max(
                        $"{nameof(ProxyDetailEntity.Stats)}.{nameof(ProxyStats.LastCallAtUtc)}",
                        delta.LastCallAtUtc);

                foreach (var stamp in expiredStamps)
                {
                    update = update.Unset(
                        $"{nameof(ProxyDetailEntity.Stats)}.{nameof(ProxyStats.Buckets)}.{stamp}");
                }

                // IsUpsert stays false: counters must never create a proxy document.
                models.Add(new UpdateOneModel<ProxyDetailEntity>(filter, update));
            }

            await collection.BulkWriteAsync(
                models, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
        }
    }
}
