using System.Runtime.CompilerServices;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DomainService.Notification
{
    /// <summary>
    /// IAM <c>Users</c> and <c>Permissions</c> of the request tenant (the root DB for an impersonated call, the same
    /// DB the notification connections live in). Reads only the fields audience rules need, in batches.
    /// </summary>
    public sealed class NotificationAudienceRepository : INotificationAudienceRepository
    {
        private const string UsersCollection = "Users";
        private const string PermissionsCollection = "Permissions";
        private const int BatchSize = 1000;

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public NotificationAudienceRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        public async IAsyncEnumerable<AudienceUser> GetActiveUsersAsync(AudienceUserQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var collection = Database().GetCollection<User>(UsersCollection);
            var projection = Builders<User>.Projection
                .Include(u => u.ItemId)
                .Include(u => u.OrganizationIds)
                .Include(u => u.Roles)
                .Include(u => u.Permissions);

            using var cursor = await collection.Find(ActiveUsersFilter(query), new FindOptions { BatchSize = BatchSize })
                .Project<AudienceUserRow>(projection)
                .ToCursorAsync(cancellationToken);
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var row in cursor.Current)
                {
                    yield return new AudienceUser(row.ItemId, row.OrganizationIds ?? [], row.Roles ?? [], row.Permissions ?? []);
                }
            }
        }

        public async Task<List<PermissionRoleGrant>> GetRoleGrantsAsync(IReadOnlyCollection<string> resources,
            CancellationToken cancellationToken = default)
        {
            if (resources.Count == 0)
                return [];

            var builder = Builders<Permission>.Filter;
            // Ne(true), not Eq(false): rows written before IsArchived existed have no such field.
            var filter = builder.In(p => p.Resource, resources) & builder.Ne(p => p.IsArchived, true);
            var projection = Builders<Permission>.Projection.Include(p => p.OrganizationId).Include(p => p.Roles);

            var rows = await Database().GetCollection<Permission>(PermissionsCollection)
                .Find(filter, new FindOptions { BatchSize = BatchSize })
                .Project<PermissionGrantRow>(projection)
                .ToListAsync(cancellationToken);

            return rows.Where(r => r.Roles is { Count: > 0 })
                       .Select(r => new PermissionRoleGrant(r.OrganizationId ?? string.Empty, r.Roles!))
                       .ToList();
        }

        /// <summary>Same active rule IAM uses for its active-user counts: a missing Status reads as Active.</summary>
        private static FilterDefinition<User> ActiveUsersFilter(AudienceUserQuery query)
        {
            var builder = Builders<User>.Filter;
            var filter = builder.Eq(u => u.Active, true)
                & (builder.Eq(u => u.Status, UserLifecycleStatus.Active) | builder.Exists(u => u.Status, false));

            if (query.UserIds.Count > 0)
                filter &= builder.In(u => u.ItemId, query.UserIds);
            if (query.OrganizationIds.Count > 0)
                filter &= builder.AnyIn(u => u.OrganizationIds, query.OrganizationIds);
            return filter;
        }

        private IMongoDatabase Database()
        {
            var context = BlocksContext.GetContext() ?? throw new InvalidOperationException("Tenant context is required.");
            return context.Impersonated
                ? _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                : _dbContextProvider.GetDatabase(context.TenantId);
        }
    }

    /// <summary>The projected user fields; only Mongo sets them.</summary>
    [BsonIgnoreExtraElements]
    internal sealed class AudienceUserRow
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public List<string>? OrganizationIds { get; set; }
        public Dictionary<string, List<string>>? Roles { get; set; }
        public Dictionary<string, List<string>>? Permissions { get; set; }
    }

    /// <summary>The projected permission fields; only Mongo sets them.</summary>
    [BsonIgnoreExtraElements]
    internal sealed class PermissionGrantRow
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string? OrganizationId { get; set; }
        public List<string>? Roles { get; set; }
    }
}
