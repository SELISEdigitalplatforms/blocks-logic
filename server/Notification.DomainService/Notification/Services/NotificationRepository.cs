using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Linq.Expressions;

namespace DomainService.Notification
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        private const string _notificationCollection = "OfflineNotifications";
        private readonly ILogger<NotificationRepository> _logger;

        public NotificationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, ILogger<NotificationRepository> logger)

        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _logger = logger;
        }

        private IMongoDatabase ResolvedClientDb()
        {
            var blocksContext = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            if (blocksContext.Impersonated)
            {
                return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            }

            return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }

        private IMongoDatabase ResolveDatabase(Func<IMongoDatabase> resolve, string source)
        {
            try
            {
                return resolve();
            }
            catch (Exception)
            {
                _logger.LogError("Notifications: the {Source} database could not be resolved", source);
                throw;
            }
        }

        /// <summary>
        /// Every database a notification for the current user could live in.
        ///
        /// <see cref="ResolvedClientDb"/> picks a single database for writes, so a notification lands in
        /// the root database when its producer was impersonating and in the tenant database otherwise.
        /// One user's notifications are therefore split across both, and reads have to look in both.
        ///
        /// Deduplication collapses the list back to one entry when the tenant already is the root
        /// database, so a single-database tenant behaves exactly as it did before.
        /// </summary>
        private List<IMongoDatabase> ResolvedClientDbs()
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new InvalidOperationException("Tenant context is required to read notifications.");

            var candidates = new List<IMongoDatabase>
            {
                ResolveDatabase(() => _dbContextProvider.GetDatabase(tenantId), "tenant"),
                ResolveDatabase(() => _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName), "root"),
            };

            var seenDatabases = new HashSet<IMongoDatabase>();
            var seenTargets = new HashSet<(IMongoClient Client, string Name)>();
            var databases = new List<IMongoDatabase>();

            foreach (var database in candidates)
            {
                if (!seenDatabases.Add(database)) continue;

                var name = database.DatabaseNamespace?.DatabaseName;
                if (name is not null && !seenTargets.Add((database.Client, name))) continue;

                databases.Add(database);
            }

            if (databases.Count == 0)
                throw new InvalidOperationException("No notification database could be resolved for the current context.");

            return databases;
        }

        private List<IMongoCollection<OfflineNotification>> NotificationCollections() =>
            [.. ResolvedClientDbs().Select(database => database.GetCollection<OfflineNotification>(_notificationCollection))];

        public void Save<T>(T data, string collectionName = "")
        {
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            collection.InsertOne(data);
        }

        public async Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var database = ResolvedClientDb();
            var collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var item = await collection.FindAsync(filter);
            return await item.FirstOrDefaultAsync();
        }

        public async Task<List<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var database = ResolvedClientDb();
            var collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var items = await collection.FindAsync(filter);
            return await items.ToListAsync();
        }

        public async Task<List<OfflineNotification>> GetNotificationItemsAcrossPlacementsAsync(
            Expression<Func<OfflineNotification, bool>> filterExpression)
        {
            var filter = Builders<OfflineNotification>.Filter.Where(filterExpression);
            var results = await Task.WhenAll(NotificationCollections().Select(async collection =>
                await (await collection.FindAsync(filter)).ToListAsync()));
            return results.SelectMany(items => items).ToList();
        }

        public async Task SaveAsync<T>(T data, string collectionName = "")
        {
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(typeof(T).Name + "s");
           await collection.InsertManyAsync(listOfData);
        }

        public async Task DeleteAsync<T>(Expression<Func<T, bool>> dataFilters)
        {
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(typeof(T).Name + "s");
            await collection.DeleteManyAsync(dataFilters);
        }

        public IQueryable<T> GetItems<T>()
        {
            var database = ResolvedClientDb();
            return database.GetCollection<T>(typeof(T).Name + "s").AsQueryable();
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId)
        {
            var builder = Builders<OfflineNotification>.Filter;

            // Match all notifications visible to this user (aligned with GetNotificationsAsync scope)
            var userNotificationFilter = builder.Or(
                builder.Eq(q => q.Payload.UserId, null),
                builder.Eq(q => q.Payload.UserId, ""),
                builder.Eq(q => q.Payload.UserId, userId)
            );

            // Exclude notifications already read by this user
            var alreadyReadFilter = builder.Where(p => p.ReadByUserIds.Contains(userId));
            var unreadFilter = userNotificationFilter & builder.Not(alreadyReadFilter);

            // First, initialize null ReadByUserIds to empty list (required for $addToSet)
            var nullReadByUserIdsFilter = unreadFilter & builder.Eq(q => q.ReadByUserIds, null);
            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds, userId);
            foreach (var collection in NotificationCollections())
            {
                await collection.UpdateManyAsync(nullReadByUserIdsFilter,
                    new UpdateDefinitionBuilder<OfflineNotification>().Set(p => p.ReadByUserIds, new List<string>()));
                // Then add userId to ReadByUserIds for all unread notifications.
                await collection.UpdateManyAsync(unreadFilter, updateDefinition);
            }
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId, string notificationId)
        {
            var builder = Builders<OfflineNotification>.Filter;
            var filter = builder.Where(p => p.Id == notificationId);

            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds,
                userId.ToString());

            foreach (var collection in NotificationCollections())
                await collection.UpdateOneAsync(filter, updateDefinition);
        }

        private static async Task<(List<OfflineNotification> Items, long Unread, long Total)> ReadNotificationPageAsync(
            IMongoCollection<OfflineNotification> collection,
            FilterDefinition<OfflineNotification> filter,
            FilterDefinition<OfflineNotification> unreadFilter,
            FindOptions<OfflineNotification> options)
        {
            var items = await (await collection.FindAsync(filter, options)).ToListAsync();
            var unread = await collection.CountDocumentsAsync(unreadFilter);
            var total = await collection.CountDocumentsAsync(filter);

            return (items, unread, total);
        }
        public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request)
        {
            var userId = BlocksContext.GetContext()?.UserId;
            if (string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException("User context is required to read notifications.");

            var builder = Builders<OfflineNotification>.Filter;
            var userFilter = builder.Where(n => !string.IsNullOrWhiteSpace(n.Payload.UserId) && n.Payload.UserId == userId);
            var unreadFilter = userFilter & builder.Where(n => !n.ReadByUserIds.Contains(userId));
            var pageFilter = request.IsUnreadOnly ? unreadFilter : userFilter;
            var skip = checked(request.PageSize * request.Page);

            var options = new FindOptions<OfflineNotification>
            {
                Skip = 0,
                Limit = checked(skip + request.PageSize),
                Sort = Builders<OfflineNotification>.Sort.Descending(n => n.CreatedTime)
            };

            var sources = await Task.WhenAll(NotificationCollections()
                .Select(collection => ReadNotificationPageAsync(collection, pageFilter, unreadFilter, options)));
            var notifications = sources.SelectMany(source => source.Items)
                .OrderByDescending(notification => notification.CreatedTime)
                .Skip(skip)
                .Take(request.PageSize)
                .ToList();

            if (!request.IsUnreadOnly)
            {
                foreach (var notification in notifications)
                {
                    notification.IsRead = notification.ReadByUserIds != null && notification.ReadByUserIds.Contains(userId);
                }
            }

            return new GetNotificationsResponse
            {
                Notifications = notifications,
                UnReadNotificationsCount = sources.Sum(source => source.Unread),
                TotalNotificationsCount = sources.Sum(source => source.Total)
            };
        }
    }
}
