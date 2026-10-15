using Blocks.Genesis;
using DataAccess.InternalService;
using DomainService.Shared;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Linq.Expressions;
using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        private const string _notificationCollection = "OfflineNotifications";
        private readonly ILogger<NotificationRepository> _logger;

        private readonly ILogicDataAccess _dataAccess;

        public NotificationRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret, ILogger<NotificationRepository> logger, ILogicDataAccess dataAccess)

        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _logger = logger;
            _dataAccess = dataAccess;
        }

        // Logic-owned notification entities. Anything else that passes through the generic methods
        // (e.g. NotificationUser on "Users", iam-owned) is not governed here and keeps the original call.
        private static string? SchemaOf<T>() => typeof(T).Name switch
        {
            nameof(OfflineNotification) => LogicSchemas.OfflineNotification,
            nameof(NotificationConnection) => LogicSchemas.NotificationConnection,
            nameof(NotificationSubscription) => LogicSchemas.NotificationSubscription,
            nameof(FirebaseConfiguration) => LogicSchemas.FirebaseConfiguration,
            _ => null,
        };

        /// <summary>
        /// The DB this call targets, for the helper. Impersonated callers read and write the ROOT DB, not the
        /// caller's tenant DB, and the resolver only loads the caller's tenant policies: pass the root DB name so
        /// the helper takes its other-tenant-db skip path. Null = the caller's own tenant DB.
        /// </summary>
        private string? TargetDb() =>
            BlocksContext.GetContext() is { Impersonated: true } ? _blocksSecret.RootDatabaseName : null;

        private async Task<ReadScope<T>> ReadAsync<T>(string schema, FilterDefinition<T> filter, string callSite) =>
            await _dataAccess.ReadAsync(schema, filter, $"NotificationRepository.{callSite}", TargetDb());

        private Task<FilterDefinition<T>?> EditAsync<T>(string schema, FilterDefinition<T> filter, string callSite) =>
            _dataAccess.EditFilterAsync(schema, filter, $"NotificationRepository.{callSite}", TargetDb());

        private async Task GateInsertAsync<T>(string callSite)
        {
            if (SchemaOf<T>() is { } schema
                && !await _dataAccess.InsertAllowedAsync(schema, $"NotificationRepository.{callSite}", TargetDb()))
                throw new UnauthorizedAccessException("Notification create is not allowed by the data access policy.");
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

        private IMongoCollection<OfflineNotification> NotificationCollection() =>
            ResolvedClientDb().GetCollection<OfflineNotification>(_notificationCollection);

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

            if (SchemaOf<T>() is { } schema)
            {
                var scope = await ReadAsync(schema, filter, "GetItem");
                return scope.Granted ? await scope.Apply(collection).FirstOrDefaultAsync() : default!;
            }

            var item = await collection.FindAsync(filter);
            return await item.FirstOrDefaultAsync();
        }

        public async Task<List<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var database = ResolvedClientDb();
            var collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            if (SchemaOf<T>() is { } schema)
            {
                var scope = await ReadAsync(schema, filter, "GetItems");
                return scope.Granted ? await scope.Apply(collection).ToListAsync() : [];
            }

            var items = await collection.FindAsync(filter);
            return await items.ToListAsync();
        }

        public async Task SaveAsync<T>(T data, string collectionName = "")
        {
            await GateInsertAsync<T>("SaveAsync");
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            await GateInsertAsync<T>("SaveAsyncMany");
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(typeof(T).Name + "s");
           await collection.InsertManyAsync(listOfData);
        }

        public async Task DeleteAsync<T>(Expression<Func<T, bool>> dataFilters)
        {
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(typeof(T).Name + "s");
            if (SchemaOf<T>() is { } schema)
            {
                var filter = await _dataAccess.DeleteFilterAsync(schema, Builders<T>.Filter.Where(dataFilters), "NotificationRepository.Delete", TargetDb());
                if (filter is null) return;
                await collection.DeleteManyAsync(filter);
                return;
            }

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
            var collection = NotificationCollection();
            var nullFilter = await EditAsync(LogicSchemas.OfflineNotification, nullReadByUserIdsFilter, "UpdateNotificationAsReadByUserId.InitList");
            if (nullFilter is not null)
            {
                await collection.UpdateManyAsync(nullFilter,
                    new UpdateDefinitionBuilder<OfflineNotification>().Set(p => p.ReadByUserIds, new List<string>()));
            }
            // Then add userId to ReadByUserIds for all unread notifications.
            var readFilter = await EditAsync(LogicSchemas.OfflineNotification, unreadFilter, "UpdateNotificationAsReadByUserId");
            if (readFilter is null) return;
            await collection.UpdateManyAsync(readFilter, updateDefinition);
        }

        public async Task UpdateNotificationAsReadByUserIdAsync(string userId, string notificationId)
        {
            var builder = Builders<OfflineNotification>.Filter;
            var filter = builder.Where(p => p.Id == notificationId);

            var updateDefinition = new UpdateDefinitionBuilder<OfflineNotification>().AddToSet(p => p.ReadByUserIds,
                userId.ToString());

            var editFilter = await EditAsync(LogicSchemas.OfflineNotification, filter, "UpdateNotificationAsReadById");
            if (editFilter is null) return;
            await NotificationCollection().UpdateOneAsync(editFilter, updateDefinition);
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

            var options = new FindOptions<OfflineNotification>
            {
                Skip = checked(request.PageSize * request.Page),
                Limit = request.PageSize,
                Sort = Builders<OfflineNotification>.Sort.Descending(n => n.CreatedTime)
            };

            var collection = NotificationCollection();
            // Two scopes (the page filter and the unread filter are different filters): 2 resolver calls here.
            var pageScope = await ReadAsync(LogicSchemas.OfflineNotification, pageFilter, "GetNotifications.Page");
            var unreadScope = await ReadAsync(LogicSchemas.OfflineNotification, unreadFilter, "GetNotifications.Unread");
            if (!pageScope.Granted || !unreadScope.Granted)
            {
                return new GetNotificationsResponse { Notifications = [], UnReadNotificationsCount = 0, TotalNotificationsCount = 0 };
            }

            // The page rows are returned as-is: the resolver projection is not applied (typed rows, own paging options).
            var notifications = await (await collection.FindAsync(pageScope.Filter, options)).ToListAsync();
            var unreadCount = await collection.CountDocumentsAsync(unreadScope.Filter);
            var totalCount = await collection.CountDocumentsAsync(pageScope.Filter);

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
                UnReadNotificationsCount = unreadCount,
                TotalNotificationsCount = totalCount
            };
        }
    }
}
