using Blocks.Genesis;
using MongoDB.Driver;

namespace DomainService.Notification
{
    /// <inheritdoc cref="INotificationUserRepository"/>
    public sealed class NotificationUserRepository : INotificationUserRepository
    {
        internal const string UsersCollection = "Users";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public NotificationUserRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        public async Task<IReadOnlyList<string>> GetOrganizationIdsAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return [];

            var user = await Users(CallerDb())
                .Find(Builders<NotificationUser>.Filter.Eq(u => u.ItemId, userId))
                .Project<NotificationUser>(Builders<NotificationUser>.Projection.Include(u => u.OrganizationIds))
                .FirstOrDefaultAsync();

            return user?.OrganizationIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList() ?? [];
        }

        public async Task<IReadOnlyDictionary<string, string>> GetLanguagesAsync(string tenantId, IReadOnlyCollection<string> userIds,
            bool rootDb, CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
                return new Dictionary<string, string>();

            var database = rootDb ? RootDb() : _dbContextProvider.GetDatabase(tenantId);
            var users = await Users(database)
                .Find(Builders<NotificationUser>.Filter.In(u => u.ItemId, userIds))
                .Project<NotificationUser>(Builders<NotificationUser>.Projection.Include(u => u.Language))
                .ToListAsync(cancellationToken);

            return users
                .Where(u => !string.IsNullOrWhiteSpace(u.ItemId) && !string.IsNullOrWhiteSpace(u.Language))
                .GroupBy(u => u.ItemId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Language!, StringComparer.Ordinal);
        }

        private static IMongoCollection<NotificationUser> Users(IMongoDatabase database) =>
            database.GetCollection<NotificationUser>(UsersCollection);

        private IMongoDatabase RootDb() => _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);

        /// <summary>An impersonated caller (root user working in a project) lives in the ROOT DB.</summary>
        private IMongoDatabase CallerDb()
        {
            var context = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            return context.Impersonated ? RootDb() : _dbContextProvider.GetDatabase(context.TenantId);
        }
    }
}
