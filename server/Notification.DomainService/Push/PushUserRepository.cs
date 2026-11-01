using System.Text.RegularExpressions;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DomainService.Notification.Push
{
    /// <summary>User names for the admin device list, from the tenant DB <c>Users</c> collection. Data access only.</summary>
    public interface IPushUserRepository
    {
        /// <summary>
        /// Ids of users whose first name, last name, email or user name holds every word of <paramref name="search"/>
        /// (case-insensitive, taken literally). At most <paramref name="limit"/>.
        /// </summary>
        Task<IReadOnlyList<string>> SearchIdsAsync(string tenantId, string search, int limit, CancellationToken cancellationToken);

        /// <summary>A display name per user found: full name, else user name, else email.</summary>
        Task<IReadOnlyDictionary<string, string>> GetNamesAsync(string tenantId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushUserRepository"/>
    public sealed class PushUserRepository : IPushUserRepository
    {
        private const int MaxWords = 5;

        private readonly IDbContextProvider _dbContextProvider;

        public PushUserRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private static FilterDefinitionBuilder<PushUserRow> Filter => Builders<PushUserRow>.Filter;

        public async Task<IReadOnlyList<string>> SearchIdsAsync(string tenantId, string search, int limit, CancellationToken cancellationToken)
        {
            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxWords).ToList();
            if (words.Count == 0)
                return [];

            var filter = Filter.And(words.Select(AnyNameField));
            return await Users(tenantId).Find(filter).Limit(limit).Project(u => u.ItemId).ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, string>> GetNamesAsync(string tenantId, IReadOnlyCollection<string> userIds,
            CancellationToken cancellationToken)
        {
            if (userIds.Count == 0)
                return new Dictionary<string, string>();

            var users = await Users(tenantId).Find(Filter.In(u => u.ItemId, userIds)).ToListAsync(cancellationToken);
            return users
                .Select(u => (u.ItemId, Name: DisplayName(u)))
                .Where(u => u.Name is not null)
                .GroupBy(u => u.ItemId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Name!, StringComparer.Ordinal);
        }

        /// <summary>The word as a literal, case-insensitive match on any name field (never a pattern from the caller).</summary>
        private static FilterDefinition<PushUserRow> AnyNameField(string word)
        {
            var pattern = new BsonRegularExpression(Regex.Escape(word), "i");
            return Filter.Regex(u => u.FirstName, pattern) | Filter.Regex(u => u.LastName, pattern)
                | Filter.Regex(u => u.Email, pattern) | Filter.Regex(u => u.UserName, pattern);
        }

        private static string? DisplayName(PushUserRow user)
        {
            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            if (fullName.Length > 0)
                return fullName;
            return string.IsNullOrWhiteSpace(user.UserName) ? user.Email : user.UserName;
        }

        private IMongoCollection<PushUserRow> Users(string tenantId) =>
            _dbContextProvider.GetDatabase(tenantId).GetCollection<PushUserRow>(NotificationUserRepository.UsersCollection);
    }

    /// <summary>Read model of the IAM <c>Users</c> document: only the name fields.</summary>
    [BsonIgnoreExtraElements]
    internal sealed class PushUserRow
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? UserName { get; set; }
    }
}
