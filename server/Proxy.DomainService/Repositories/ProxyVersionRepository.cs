using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis;
using MongoDB.Driver;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IProxyVersionRepository"/>. The <c>{ ProxyId: 1, VersionNumber: -1 }</c>
    /// index is created lazily on first use per tenant, best-effort.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class ProxyVersionRepository : IProxyVersionRepository
    {
        private const string CollectionName = "ProxyVersions";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly ConcurrentDictionary<string, byte> _indexedTenants = new(StringComparer.Ordinal);

        public ProxyVersionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
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

            try
            {
                var keys = Builders<ProxyVersionEntity>.IndexKeys
                    .Ascending(v => v.ProxyId)
                    .Descending(v => v.VersionNumber);
                await collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<ProxyVersionEntity>(keys, new CreateIndexOptions { Name = "ix_proxy_version" }));
            }
            catch (MongoCommandException)
            {
                _indexedTenants.TryRemove(tenantId, out _);
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
