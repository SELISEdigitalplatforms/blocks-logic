using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// Texts of some keys of the Localization module <c>notifications</c>, per culture, and the project's default
    /// language. <see cref="ByKey"/>: key name → culture → text.
    /// </summary>
    public sealed record PushKeyTexts(string? DefaultLanguage, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByKey)
    {
        public static PushKeyTexts Empty { get; } = new(null, new Dictionary<string, IReadOnlyDictionary<string, string>>());
    }

    /// <summary>Reads blocks-localization's collections in the tenant DB. Data access only.</summary>
    public interface IPushLocalizationRepository
    {
        Task<PushKeyTexts> GetTextsAsync(string tenantId, IReadOnlyCollection<string> keyNames, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushLocalizationRepository"/>
    /// <remarks>Same collections and fields as blocks-localization: BlocksLanguageModules (ModuleName), BlocksLanguageKeys
    /// (KeyName, ModuleId, Resources[{Culture, Value}]), BlocksLanguages (LanguageCode, IsDefault).</remarks>
    public sealed class PushLocalizationRepository : IPushLocalizationRepository
    {
        public const string ModuleName = "notifications";

        private readonly IDbContextProvider _dbContextProvider;

        public PushLocalizationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<PushKeyTexts> GetTextsAsync(string tenantId, IReadOnlyCollection<string> keyNames, CancellationToken cancellationToken)
        {
            var database = _dbContextProvider.GetDatabase(tenantId);
            var defaultLanguage = await database.GetCollection<LanguageRow>("BlocksLanguages")
                .Find(Builders<LanguageRow>.Filter.Eq(l => l.IsDefault, true))
                .FirstOrDefaultAsync(cancellationToken);

            var module = await database.GetCollection<ModuleRow>("BlocksLanguageModules")
                .Find(Builders<ModuleRow>.Filter.Eq(m => m.ModuleName, ModuleName))
                .FirstOrDefaultAsync(cancellationToken);
            if (module is null || keyNames.Count == 0)
                return new PushKeyTexts(defaultLanguage?.LanguageCode, PushKeyTexts.Empty.ByKey);

            var keys = await database.GetCollection<KeyRow>("BlocksLanguageKeys")
                .Find(Builders<KeyRow>.Filter.Eq(k => k.ModuleId, module.ItemId) & Builders<KeyRow>.Filter.In(k => k.KeyName, keyNames))
                .ToListAsync(cancellationToken);

            var byKey = keys
                .GroupBy(k => k.KeyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Cultures(g.First()), StringComparer.Ordinal);
            return new PushKeyTexts(defaultLanguage?.LanguageCode, byKey);
        }

        private static IReadOnlyDictionary<string, string> Cultures(KeyRow key) =>
            (key.Resources ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Culture) && !string.IsNullOrWhiteSpace(r.Value))
                .GroupBy(r => r.Culture!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value!, StringComparer.OrdinalIgnoreCase);
    }

    // Read models of blocks-localization documents (only the fields push needs).
    [BsonIgnoreExtraElements]
    internal sealed class LanguageRow
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string? LanguageCode { get; set; }
        public bool IsDefault { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class ModuleRow
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string? ModuleName { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class KeyRow
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string KeyName { get; set; } = string.Empty;
        public string? ModuleId { get; set; }
        public List<ResourceRow>? Resources { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class ResourceRow
    {
        public string? Culture { get; set; }
        public string? Value { get; set; }
    }
}
