using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Proxy.DomainService.Entities;

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
        private readonly ConcurrentDictionary<string, byte> _indexedTenants = new(StringComparer.Ordinal);

        public ProxyRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

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
            catch (MongoCommandException)
            {
                _indexedTenants.TryRemove(tenantId, out _);
            }
        }

        private static FilterDefinition<ProxyDetailEntity> BuildListFilter(string tenantId, string? search, bool? enabled)
        {
            var builder = Builders<ProxyDetailEntity>.Filter;
            var filter = builder.Eq(p => p.TenantId, tenantId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
                filter &= builder.Or(builder.Regex(p => p.Name, pattern), builder.Regex(p => p.Slug, pattern));
            }

            if (enabled.HasValue)
            {
                filter &= builder.Eq(p => p.Enabled, enabled.Value);
            }

            return filter;
        }

        public async Task<ProxyDetailEntity?> GetAsync(string tenantId, string itemId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.TenantId, tenantId)
                         & Builders<ProxyDetailEntity>.Filter.Eq(p => p.ItemId, itemId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<ProxyDetailEntity?> GetBySlugAsync(string tenantId, string slug)
        {
            var collection = GetCollection(tenantId);
            await EnsureIndexesAsync(tenantId, collection);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.TenantId, tenantId)
                         & Builders<ProxyDetailEntity>.Filter.Eq(p => p.Slug, slug);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<ProxyDetailEntity>> GetAllAsync(
            string tenantId, string? search, bool? enabled, int pageSize, int pageNumber)
        {
            var collection = GetCollection(tenantId);
            return await collection
                .Find(BuildListFilter(tenantId, search, enabled))
                .SortByDescending(p => p.CreatedDate)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<long> CountAsync(string tenantId, string? search, bool? enabled)
        {
            var collection = GetCollection(tenantId);
            return await collection.CountDocumentsAsync(BuildListFilter(tenantId, search, enabled));
        }

        public async Task InsertAsync(ProxyDetailEntity proxy)
        {
            var collection = GetCollection(proxy.TenantId);
            await EnsureIndexesAsync(proxy.TenantId, collection);
            await collection.InsertOneAsync(proxy);
        }

        public async Task ReplaceAsync(ProxyDetailEntity proxy)
        {
            var collection = GetCollection(proxy.TenantId);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.TenantId, proxy.TenantId)
                         & Builders<ProxyDetailEntity>.Filter.Eq(p => p.ItemId, proxy.ItemId);
            await collection.ReplaceOneAsync(filter, proxy);
        }

        public async Task DeleteAsync(string tenantId, string itemId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<ProxyDetailEntity>.Filter.Eq(p => p.TenantId, tenantId)
                         & Builders<ProxyDetailEntity>.Filter.Eq(p => p.ItemId, itemId);
            await collection.DeleteOneAsync(filter);
        }
    }
}
