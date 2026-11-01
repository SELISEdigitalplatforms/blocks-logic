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

        /// <summary>Defaults, rules and the requester's access lists for records about to be inserted.</summary>
        private async Task PrepareInsertAsync<T>(IEnumerable<T> records, string callSite)
        {
            if (SchemaOf<T>() is not { } schema)
                return;
            foreach (var record in records)
                await _dataAccess.PrepareWriteAsync(schema, record, isInsert: true, $"NotificationRepository.{callSite}", TargetDb());
        }

        /// <summary>
        /// Impersonated callers (a root tenant user working in a project) use the ROOT DB for everything,
        /// hub connections and subscriptions included: the user lives in root, and the Blocks services
        /// send their notifications with the root key, so they look the connections up in root.
        /// </summary>
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
            await PrepareInsertAsync([data], "SaveAsync");
            var database = ResolvedClientDb();
            IMongoCollection<T> collection = database.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            await GateInsertAsync<T>("SaveAsyncMany");
            await PrepareInsertAsync(listOfData, "SaveAsyncMany");
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

        public async Task MarkAllAsReadAsync(InboxViewer viewer)
        {
            var builder = Builders<OfflineNotification>.Filter;
            var unreadFilter = InboxFilters.Visible(viewer, includeLegacyBroadcast: true) & InboxFilters.Unread(viewer);
            var collection = NotificationCollection();

            // $addToSet needs an array: first turn a missing or null ReadByUserIds into an empty list.
            var nullFilter = await EditAsync(LogicSchemas.OfflineNotification,
                unreadFilter & builder.Eq(q => q.ReadByUserIds, null), "MarkAllAsRead.InitList");
            if (nullFilter is not null)
            {
                await collection.UpdateManyAsync(nullFilter,
                    Builders<OfflineNotification>.Update.Set(p => p.ReadByUserIds, new List<string>()));
            }

            var readFilter = await EditAsync(LogicSchemas.OfflineNotification, unreadFilter, "MarkAllAsRead");
            if (readFilter is null) return;
            await collection.UpdateManyAsync(readFilter, Builders<OfflineNotification>.Update.AddToSet(p => p.ReadByUserIds, viewer.UserId));
        }

        /// <summary>
        /// Adds only the viewer to the row's ReadByUserIds, so an organization row read by one user stays unread for
        /// the others. A row the viewer cannot see is not touched.
        /// </summary>
        public async Task MarkAsReadAsync(InboxViewer viewer, string notificationId)
        {
            var filter = Builders<OfflineNotification>.Filter.Eq(p => p.Id, notificationId)
                & InboxFilters.Visible(viewer, includeLegacyBroadcast: true);

            var editFilter = await EditAsync(LogicSchemas.OfflineNotification, filter, "MarkAsRead");
            if (editFilter is null) return;
            await NotificationCollection().UpdateOneAsync(editFilter,
                Builders<OfflineNotification>.Update.AddToSet(p => p.ReadByUserIds, viewer.UserId));
        }

        public async Task<List<OfflineNotification>> GetInboxItemsAsync(FilterDefinition<OfflineNotification> filter)
        {
            var scope = await ReadAsync(LogicSchemas.OfflineNotification, filter, "GetInboxItems");
            return scope.Granted ? await scope.Apply(NotificationCollection()).ToListAsync() : [];
        }

        public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request, InboxViewer viewer)
        {
            var visibleFilter = InboxFilters.Visible(viewer, includeLegacyBroadcast: false);
            var unreadFilter = visibleFilter & InboxFilters.Unread(viewer);
            var pageFilter = request.IsUnreadOnly ? unreadFilter : visibleFilter;

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

            return new GetNotificationsResponse
            {
                Notifications = InboxRows.ForViewer(notifications, viewer.UserId),
                UnReadNotificationsCount = unreadCount,
                TotalNotificationsCount = totalCount
            };
        }
    }
}
