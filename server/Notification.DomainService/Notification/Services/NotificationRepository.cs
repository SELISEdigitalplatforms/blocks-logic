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
        private IMongoDatabase _clientDb;

        private const string _notificationCollection = "OfflineNotifications";
        private const string _rootDatabaseName = "BlocksRootDb";
        private readonly ILogger<NotificationRepository> _logger;

        public NotificationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, ILogger<NotificationRepository> logger)

        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _logger = logger;
        }

        private IMongoDatabase ResolvedClientDb()
        {
            var blocksContext = BlocksContext.GetContext();
                _logger.LogInformation($"Blocks Context {blocksContext.ToString()}");
            if (blocksContext.Impersonated)
                {
                    return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _rootDatabaseName);
                }

                return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }

        private IMongoDatabase? TryGetDatabase(Func<IMongoDatabase> resolve, string source)
        {
            try
            {
                return resolve();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notifications: the {Source} database could not be resolved and was skipped", source);
                return null;
            }
        }

        /// <summary>
        /// Every database a notification for the current user could live in.
        ///
        /// <see cref="ResolvedClientDb"/> picks a single database for writes, so a notification lands in
        /// the root database when its producer was impersonating and in the tenant database otherwise.
        /// One user's notifications are therefore split across both, and reads have to look in both.
        ///
        /// The two dedupe passes collapse the list back to one entry when the tenant already is the root
        /// database, so a single-database tenant behaves exactly as it did before.
        /// </summary>
        private List<IMongoDatabase> ResolvedClientDbs()
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;

            var candidates = new List<IMongoDatabase?>
            {
                string.IsNullOrWhiteSpace(tenantId)
                    ? null
                    : TryGetDatabase(() => _dbContextProvider.GetDatabase(tenantId), "tenant"),
                TryGetDatabase(() => _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _rootDatabaseName), "root"),
            };

            var seenDatabases = new HashSet<IMongoDatabase>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var databases = new List<IMongoDatabase>();

            foreach (var database in candidates)
            {
                if (database is null || !seenDatabases.Add(database)) continue;

                var name = database.DatabaseNamespace?.DatabaseName;
                if (name is not null && !seenNames.Add(name)) continue;

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
            _clientDb = ResolvedClientDb();
            IMongoCollection<T> collection = _clientDb.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            collection.InsertOne(data);
        }

        public async Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            _clientDb = ResolvedClientDb();
            var collection = _clientDb.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var item = await collection.FindAsync(filter);
            return await item.FirstOrDefaultAsync();
        }

        public async Task<List<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            _clientDb = ResolvedClientDb();
            var collection = _clientDb.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var items = await collection.FindAsync(filter);
            return await items.ToListAsync();
        }

        public async Task SaveAsync<T>(T data, string collectionName = "")
        {
            _clientDb = ResolvedClientDb();
            IMongoCollection<T> collection = _clientDb.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            _clientDb = ResolvedClientDb();
            IMongoCollection<T> collection = _clientDb.GetCollection<T>(typeof(T).Name + "s");
           await collection.InsertManyAsync(listOfData);
        }

        public async Task DeleteAsync<T>(Expression<Func<T, bool>> dataFilters)
        {
            _clientDb = ResolvedClientDb();
            IMongoCollection<T> collection = _clientDb.GetCollection<T>(typeof(T).Name + "s");
            await collection.DeleteManyAsync(dataFilters);
        }

        public IQueryable<T> GetItems<T>()
        {
            _clientDb = ResolvedClientDb();
            return _clientDb.GetCollection<T>(typeof(T).Name + "s").AsQueryable();
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId)
        {
            _clientDb = ResolvedClientDb();
            var builder = Builders<OfflineNotification>.Filter;
            var collection = _clientDb.GetCollection<OfflineNotification>(_notificationCollection);

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
            await collection.UpdateManyAsync(nullReadByUserIdsFilter,
                new UpdateDefinitionBuilder<OfflineNotification>().Set(p => p.ReadByUserIds, new List<string>()));

            // Then add userId to ReadByUserIds for all unread notifications
            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds, userId);
            await collection.UpdateManyAsync(unreadFilter, updateDefinition);
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId, string notificationId)
        {
            _clientDb = ResolvedClientDb();
            var builder = Builders<OfflineNotification>.Filter;
            var filter = builder.Where(p => p.Id == notificationId);

            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds,
                userId.ToString());

            await _clientDb.GetCollection<OfflineNotification>(_notificationCollection).UpdateOneAsync(filter, updateDefinition);
        }

        //public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request)
        //{
        //    var builder = Builders<OfflineNotification>.Filter;
        //    var filter = FilterDefinition<OfflineNotification>.Empty;
        //    var userId = BlocksContext.GetContext()?.UserId;

        //    if (request.IsUnreadOnly)
        //        filter = builder.Where(n => !n.ReadByUserIds.Contains(userId));

        //    filter = filter & builder.Where(n => !string.IsNullOrWhiteSpace(n.Payload.UserId) && n.Payload.UserId == userId);

        //    var unreadFilter = filter & builder.Where(n => !n.ReadByUserIds.Contains(userId));
        //    var skip = request.PageSize * request.Page;

        //    // Each source is asked for skip + limit rows from the top so that the merged, re-sorted
        //    // sequence still contains every candidate for the requested page.
        //    var options = new FindOptions<OfflineNotification>
        //    {
        //        Skip = 0,
        //        Limit = skip + request.PageSize,
        //        Sort = Builders<OfflineNotification>.Sort.Descending(n => n.CreatedTime)
        //    };

        //    var sources = await Task.WhenAll(NotificationCollections()
        //        .Select(collection => ReadNotificationPageAsync(collection, filter, unreadFilter, options)));

        //    var notifications = sources
        //        .SelectMany(source => source.Items)
        //        .GroupBy(n => n.Id)
        //        .Select(duplicates => duplicates.First())
        //        .OrderByDescending(n => n.CreatedTime)
        //        .Skip(skip)
        //        .Take(request.PageSize)
        //        .ToList();

        //    if (!request.IsUnreadOnly)
        //    {
        //        foreach (var notification in notifications)
        //        {
        //            notification.IsRead = notification.ReadByUserIds != null && notification.ReadByUserIds.Contains(userId);
        //        }
        //    }

        //    return new GetNotificationsResponse
        //    {
        //        Notifications = notifications,
        //        UnReadNotificationsCount = sources.Sum(source => source.Unread),
        //        TotalNotificationsCount = sources.Sum(source => source.Total)
        //    };
        //}

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
        public async Task<GetNotificationsResponse> GetNotificationsAsync ( GetNotificationsRequest request )
        {
            var collection = _clientDb.GetCollection<OfflineNotification>("OfflineNotifications");
            var builder = Builders<OfflineNotification>.Filter;
            var filter = FilterDefinition<OfflineNotification>.Empty;
            var userId = BlocksContext.GetContext()?.UserId;

            if (request.IsUnreadOnly)
                filter = builder.Where(n => !n.ReadByUserIds.Contains(userId));

            filter = filter & builder.Where(n => !string.IsNullOrWhiteSpace(n.Payload.UserId) && n.Payload.UserId == userId);

            var options = new FindOptions<OfflineNotification>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<OfflineNotification>.Sort.Descending(n => n.CreatedTime)
            };

            var notifications = await ( await collection.FindAsync(filter, options) ).ToListAsync();
            var unReadNotificationsCount = await collection.CountDocumentsAsync(filter & builder.Where(n => !n.ReadByUserIds.Contains(userId)));
            var totalNotificationCount = await collection.CountDocumentsAsync(filter);

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
                UnReadNotificationsCount = unReadNotificationsCount,
                TotalNotificationsCount = totalNotificationCount
            };
        }
    }
}
