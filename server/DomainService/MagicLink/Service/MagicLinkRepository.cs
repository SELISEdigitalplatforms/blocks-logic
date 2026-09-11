using Blocks.Genesis;
using DomainService.MagicLink.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainService.MagicLink.Service
{
    /// <summary>
    /// Repository implementation for MagicLink data operations
    /// </summary>
    public class MagicLinkRepository : IMagicLinkRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public MagicLinkRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        /// <summary>
        /// Gets a MongoDB collection from the current tenant's database.
        /// The tenant comes from BlocksContext (populated from the X-Blocks-Key header),
        /// so the database itself provides isolation.
        /// </summary>
        private IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            return _dbContextProvider.GetCollection<T>(tenantId, collectionName);
        }

        #region MagicLink Operations

        public async Task<Models.MagicLink?> GetMagicLinkAsync(string itemId)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            var filterBuilder = Builders<Models.MagicLink>.Filter;
            var filter = filterBuilder.Eq(x => x.ItemId, itemId);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<string> CreateMagicLinkAsync(Models.MagicLink link)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            await collection.InsertOneAsync(link);
            return link.ItemId;
        }

        public async Task<bool> UpdateMagicLinkAsync(Models.MagicLink link)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            var filter = Builders<Models.MagicLink>.Filter.Eq(x => x.ItemId, link.ItemId);
            link.UpdatedAt = DateTime.UtcNow;
            var result = await collection.ReplaceOneAsync(filter, link);
            return result.ModifiedCount > 0;
        }

        public async Task<List<Models.MagicLink>> GetMagicLinksByIdsAsync(List<string> itemIds)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            var filterBuilder = Builders<Models.MagicLink>.Filter;
            var filter = filterBuilder.In(x => x.ItemId, itemIds);

            return await collection.Find(filter).ToListAsync();
        }

        public async Task<(List<Models.MagicLink> links, int totalCount)> GetMagicLinksAsync(GetMagicLinksRequest request)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            var filterBuilder = Builders<Models.MagicLink>.Filter;
            var filters = new List<FilterDefinition<Models.MagicLink>>();

            // Optional: Filter by Type
            if (request.Type.HasValue)
            {
                filters.Add(filterBuilder.Eq(x => x.Type, request.Type.Value));
            }

            // Optional: Search filter for Name and Uri
            if (!string.IsNullOrEmpty(request.SearchText))
            {
                var searchFilter = filterBuilder.Or(
                    filterBuilder.Regex("Name", new BsonRegularExpression($".*{request.SearchText}.*", "i")),
                    filterBuilder.Regex("Uri", new BsonRegularExpression($".*{request.SearchText}.*", "i"))
                );
                filters.Add(searchFilter);
            }

            // Optional: Filter by RequestMethod (for Action type)
            if (!string.IsNullOrEmpty(request.RequestMethod))
            {
                filters.Add(filterBuilder.Eq(x => x.RequestMethod, request.RequestMethod.ToUpperInvariant()));
            }

            // Optional: Filter by ExpiryDate range
            var expiryDateFilters = BuildExpiryDateFilter(request, filterBuilder);
            filters.AddRange(expiryDateFilters);

            // Optional: Filter by Status
            var statusFilter = BuildStatusFilter(request, filterBuilder);
            if (statusFilter != null)
            {
                filters.Add(statusFilter);
            }

            // Combine all filters
            var combinedFilter = filterBuilder.And(filters);

            var totalCount = (int)await collection.CountDocumentsAsync(combinedFilter);
            var links = await collection.Find(combinedFilter)
                .Sort(Builders<Models.MagicLink>.Sort.Descending(x => x.CreatedAt))
                .Skip(request.PageNumber * request.PageSize)
                .Limit(request.PageSize)
                .ToListAsync();

            return (links, totalCount);
        }

        public async Task<Models.MagicLink?> IncrementUsageCountAsync(string linkId)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            var filter = Builders<Models.MagicLink>.Filter.Eq(x => x.ItemId, linkId);
            var update = Builders<Models.MagicLink>.Update
                .Inc(x => x.UsageCount, 1)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<Models.MagicLink>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await collection.FindOneAndUpdateAsync(filter, update, options);
        }

        public async Task<bool> MarkAsExpiredAsync(string linkId, MagicLinkExpiredReason reason)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.Constants.MagicLinksCollection);
            var filter = Builders<Models.MagicLink>.Filter.Eq(x => x.ItemId, linkId);
            var update = Builders<Models.MagicLink>.Update
                .Set(x => x.IsExpired, true)
                .Set(x => x.ExpiredReason, reason.ToString())
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region ClientCredentials and Config Operations

        public async Task<ClientCredential?> GetClientCredentialsAsync(string clientCredentialId, string tenantId)
        {
            var database = _dbContextProvider.GetDatabase(tenantId);
            var collection = database.GetCollection<ClientCredential>(Utilities.Constants.ClientCredentialsCollection);
            var filter = Builders<ClientCredential>.Filter.Eq(x => x.ItemId, clientCredentialId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<LinkBasedActionConfig?> GetLinkConfigAsync(string configId, string tenantId)
        {
            var database = _dbContextProvider.GetDatabase(tenantId);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.Constants.LinkBasedActionConfigsCollection);
            var filter = Builders<LinkBasedActionConfig>.Filter.Eq(x => x.ItemId, configId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Builds filter for ExpiryDate range
        /// </summary>
        private static List<FilterDefinition<Models.MagicLink>> BuildExpiryDateFilter(GetMagicLinksRequest request, FilterDefinitionBuilder<Models.MagicLink> filterBuilder)
        {
            var dateFilters = new List<FilterDefinition<Models.MagicLink>>();

            if (request.ExpiryDateRange == null)
            {
                return dateFilters;
            }

            var hasStartDate = request.ExpiryDateRange.StartDate != default(DateTime) && request.ExpiryDateRange.StartDate != null;
            var hasEndDate = request.ExpiryDateRange.EndDate != default(DateTime) && request.ExpiryDateRange.EndDate != null;

            if (hasStartDate && !hasEndDate)
            {
                dateFilters.Add(filterBuilder.Gte(x => x.ExpiryDate, request.ExpiryDateRange.StartDate));
            }
            else if (!hasStartDate && hasEndDate)
            {
                dateFilters.Add(filterBuilder.Lte(x => x.ExpiryDate, request.ExpiryDateRange.EndDate));
            }
            else if (hasStartDate && hasEndDate)
            {
                dateFilters.Add(filterBuilder.And(
                    filterBuilder.Gte(x => x.ExpiryDate, request.ExpiryDateRange.StartDate),
                    filterBuilder.Lte(x => x.ExpiryDate, request.ExpiryDateRange.EndDate)
                ));
            }

            return dateFilters;
        }

        /// <summary>
        /// Builds filter for Status based on business rules.
        /// </summary>
        private static FilterDefinition<Models.MagicLink>? BuildStatusFilter(GetMagicLinksRequest request, FilterDefinitionBuilder<Models.MagicLink> filterBuilder)
        {
            if (string.IsNullOrEmpty(request.Status))
            {
                return null;
            }

            var now = DateTime.UtcNow;

            return request.Status switch
            {
                "ManuallyDisabled" => filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.ManuallyDisabled.ToString()),

                "UsageLimitExceeded" => filterBuilder.Or(
                    filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.UsageLimitExceeded.ToString()),
                    filterBuilder.And(
                        filterBuilder.Gt(x => x.UsageLimit, 0),
                        filterBuilder.Where(x => x.UsageCount >= x.UsageLimit)
                    )
                ),

                "TimeExpired" or "LifespanExpired" => filterBuilder.Or(
                    filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.TimeExpired.ToString()),
                    filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.LifespanExpired.ToString()),
                    filterBuilder.And(
                        filterBuilder.Ne(x => x.ExpiryDate, null),
                        filterBuilder.Lt(x => x.ExpiryDate, now),
                        filterBuilder.Eq(x => x.IsExpired, false)
                    )
                ),

                "Active" => filterBuilder.And(
                    filterBuilder.Eq(x => x.IsExpired, false),
                    filterBuilder.Or(
                        filterBuilder.Eq(x => x.UsageLimit, 0),
                        filterBuilder.Where(x => x.UsageCount < x.UsageLimit)
                    ),
                    filterBuilder.Or(
                        filterBuilder.Eq(x => x.ExpiryDate, null),
                        filterBuilder.Gte(x => x.ExpiryDate, now)
                    )
                ),

                _ => null
            };
        }

        #endregion

        #region Visitor Usage Operations

        public async Task CreateVisitorUsageAsync(MagicLinkVisitorUsage visitorUsage)
        {
            var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId);
            var collection = database.GetCollection<MagicLinkVisitorUsage>(Utilities.Constants.MagicLinkVisitorUsagesCollection);
            await collection.InsertOneAsync(visitorUsage);
        }

        #endregion

        #region LinkBasedActionConfig Operations

        public async Task<LinkBasedActionConfig?> GetLinkBasedActionConfigAsync()
        {
            var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.Constants.LinkBasedActionConfigsCollection);
            return await collection.Find(Builders<LinkBasedActionConfig>.Filter.Empty).FirstOrDefaultAsync();
        }

        public async Task<string> CreateLinkBasedActionConfigAsync(LinkBasedActionConfig config)
        {
            var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.Constants.LinkBasedActionConfigsCollection);
            await collection.InsertOneAsync(config);
            return config.ItemId;
        }

        public async Task<bool> UpdateLinkBasedActionConfigAsync(LinkBasedActionConfig config)
        {
            var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.Constants.LinkBasedActionConfigsCollection);
            var filter = Builders<LinkBasedActionConfig>.Filter.Eq(x => x.ItemId, config.ItemId);
            config.UpdatedAt = DateTime.UtcNow;
            var result = await collection.ReplaceOneAsync(filter, config);
            return result.ModifiedCount > 0;
        }

        #endregion
    }
}

