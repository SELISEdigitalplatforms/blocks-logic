using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IProxyVersionRepository"/>. The unique <c>{ ProxyId: 1, VersionNumber: -1 }</c>
    /// index is created lazily on first use per tenant, best-effort: two history rows with the same version
    /// number for one proxy cannot exist (PX-16).
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class ProxyVersionRepository : IProxyVersionRepository
    {
        private const string CollectionName = "ProxyVersions";

        private readonly IDbContextProvider _dbContextProvider;
        private const string OldIndexName = "ix_proxy_version";
        private const string UniqueIndexName = "ux_proxy_version";

        private readonly ILogger<ProxyVersionRepository> _logger;
        private readonly ConcurrentDictionary<string, byte> _indexedTenants = new(StringComparer.Ordinal);

        public ProxyVersionRepository(IDbContextProvider dbContextProvider, ILogger<ProxyVersionRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoCollection<ProxyVersionEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<ProxyVersionEntity>(tenantId, CollectionName);
        }

        private async Task EnsureIndexesAsync(string tenantId, IMongoCollection<ProxyVersionEntity> collection)
        {
            if (!_indexedTenants.TryAdd(tenantId, 0))
            {
                return;
            }

            var keys = Builders<ProxyVersionEntity>.IndexKeys
                .Ascending(v => v.ProxyId)
                .Descending(v => v.VersionNumber);
            try
            {
                // Drop the old non-unique index on the same keys, so only the unique one is left to maintain.
                await DropOldIndexAsync(collection);
                await collection.Indexes.CreateOneAsync(new CreateIndexModel<ProxyVersionEntity>(
                    keys, new CreateIndexOptions { Unique = true, Name = UniqueIndexName }));
            }
            catch (MongoCommandException ex)
            {
                // Most likely old duplicate version rows. Keep the lookup fast with the plain index and say so;
                // the version-checked proxy writes still stop new duplicates.
                _logger.LogWarning(
                    ex,
                    "Proxy versions: could not create the unique index '{Index}' for tenant {TenantId}; using the "
                    + "non-unique one. Remove duplicate (ProxyId, VersionNumber) rows to enable it.",
                    UniqueIndexName, tenantId);
                try
                {
                    await collection.Indexes.CreateOneAsync(new CreateIndexModel<ProxyVersionEntity>(
                        keys, new CreateIndexOptions { Name = OldIndexName }));
                }
                catch (MongoCommandException)
                {
                    _indexedTenants.TryRemove(tenantId, out _);
                }
            }
        }

        private static async Task DropOldIndexAsync(IMongoCollection<ProxyVersionEntity> collection)
        {
            using var cursor = await collection.Indexes.ListAsync();
            var names = (await cursor.ToListAsync()).Select(i => i["name"].AsString);
            if (names.Contains(OldIndexName, StringComparer.Ordinal))
            {
                await collection.Indexes.DropOneAsync(OldIndexName);
            }
        }

        public async Task InsertAsync(ProxyVersionEntity version)
        {
            var collection = GetCollection(version.TenantId);
            await EnsureIndexesAsync(version.TenantId, collection);
            await collection.InsertOneAsync(version);
        }

        public async Task<ProxyVersionEntity?> GetAsync(string tenantId, string versionId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyVersionEntity>.Filter.Eq(v => v.TenantId, tenantId)
                         & Builders<ProxyVersionEntity>.Filter.Eq(v => v.ItemId, versionId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<ProxyVersionEntity>> GetForProxyAsync(
            string tenantId, string proxyId, int pageSize, int pageNumber)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyVersionEntity>.Filter.Eq(v => v.TenantId, tenantId)
                         & Builders<ProxyVersionEntity>.Filter.Eq(v => v.ProxyId, proxyId);
            return await collection
                .Find(filter)
                .SortByDescending(v => v.VersionNumber)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<long> CountForProxyAsync(string tenantId, string proxyId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyVersionEntity>.Filter.Eq(v => v.TenantId, tenantId)
                         & Builders<ProxyVersionEntity>.Filter.Eq(v => v.ProxyId, proxyId);
            return await collection.CountDocumentsAsync(filter);
        }
    }
}
