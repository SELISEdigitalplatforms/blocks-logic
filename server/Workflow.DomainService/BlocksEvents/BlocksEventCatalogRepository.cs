using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Workflow.DomainService.BlocksEvents
{
    /// <summary>
    /// The tenant's Blocks Event Trigger catalog: one <c>KeyValueStores</c> document keyed
    /// <see cref="BlocksEventTriggerConstants.CatalogKey"/> whose <c>Value</c> is an array of
    /// <see cref="BlocksEventCatalogEntry"/>. Genesis reads the same document to decide what to forward.
    /// Every call names its tenant, so one tenant's catalog is never read or written for another.
    /// </summary>
    public interface IBlocksEventCatalogRepository
    {
        /// <summary>The catalog's entries, or <c>null</c> when the tenant has no catalog document (not seeded).</summary>
        Task<IReadOnlyList<BlocksEventCatalogEntry>?> GetAsync(string tenantId);

        /// <summary>
        /// Sets the given flags on the one entry named <paramref name="eventName"/>, atomically, so concurrent
        /// toggles of other events or other flags are never lost. Entries are never added, removed or renamed.
        /// </summary>
        Task<BlocksEventFlagUpdateResult> UpdateFlagsAsync(
            string tenantId, string eventName, bool? isValidForWorkflow, bool? isValidForFunction, string? userId);
    }

    public enum BlocksEventFlagUpdateResult
    {
        Updated,
        CatalogNotSeeded,
        EventNotInCatalog,
    }

    public sealed class BlocksEventCatalogRepository : IBlocksEventCatalogRepository
    {
        public const string CollectionName = "KeyValueStores";

        private readonly IDbContextProvider _dbContextProvider;

        public BlocksEventCatalogRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoCollection<BsonDocument> Collection(string tenantId) =>
            _dbContextProvider.GetDatabase(tenantId).GetCollection<BsonDocument>(CollectionName);

        private static FilterDefinition<BsonDocument> CatalogFilter =>
            Builders<BsonDocument>.Filter.Eq("Key", BlocksEventTriggerConstants.CatalogKey);

        public async Task<IReadOnlyList<BlocksEventCatalogEntry>?> GetAsync(string tenantId)
        {
            var document = await Collection(tenantId).Find(CatalogFilter)
                .Project(Builders<BsonDocument>.Projection.Include("Value"))
                .FirstOrDefaultAsync();
            if (document is null) return null;
            return document.TryGetValue("Value", out var value) ? ReadEntries(value) : [];
        }

        public async Task<BlocksEventFlagUpdateResult> UpdateFlagsAsync(
            string tenantId, string eventName, bool? isValidForWorkflow, bool? isValidForFunction, string? userId)
        {
            var collection = Collection(tenantId);
            var (filter, update, options) = BuildFlagUpdate(eventName, isValidForWorkflow, isValidForFunction, userId, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(filter, update, options);

            // The filter names the event, so a match means it was set (setting the current value is a
            // success too). No match: tell a missing catalog from a name the catalog does not have.
            if (result.MatchedCount > 0) return BlocksEventFlagUpdateResult.Updated;
            return await collection.CountDocumentsAsync(CatalogFilter, new CountOptions { Limit = 1 }) == 0
                ? BlocksEventFlagUpdateResult.CatalogNotSeeded
                : BlocksEventFlagUpdateResult.EventNotInCatalog;
        }

        /// <summary>
        /// Filter <c>{ Key, "Value.EventName": name }</c>; <c>$set Value.$[e].&lt;flag&gt;</c> with
        /// <c>arrayFilters [{ "e.EventName": name }]</c>, plus the document's LastUpdatedDate and LastUpdatedBy.
        /// Public for the rendered-update test.
        /// </summary>
        public static (FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update, UpdateOptions Options) BuildFlagUpdate(
            string eventName, bool? isValidForWorkflow, bool? isValidForFunction, string? userId, DateTime now)
        {
            var filter = CatalogFilter & Builders<BsonDocument>.Filter.Eq("Value.EventName", eventName);

            var set = new BsonDocument();
            if (isValidForWorkflow is bool workflow)
                set[$"Value.$[e].{nameof(BlocksEventCatalogEntry.IsValidForWorkflow)}"] = workflow;
            if (isValidForFunction is bool function)
                set[$"Value.$[e].{nameof(BlocksEventCatalogEntry.IsValidForFunction)}"] = function;
            set["LastUpdatedDate"] = now;
            set["LastUpdatedBy"] = userId is null ? BsonNull.Value : userId;

            var options = new UpdateOptions
            {
                ArrayFilters =
                [
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("e.EventName", eventName)),
                ],
            };
            return (filter, new BsonDocument("$set", set), options);
        }

        /// <summary>
        /// Same rules as Genesis's catalog cache: each element is read on its own, and one that is not a
        /// document, has no string EventName, or has a non-bool flag is skipped, so a bad entry never hides
        /// the rest of the catalog.
        /// </summary>
        public static IReadOnlyList<BlocksEventCatalogEntry> ReadEntries(BsonValue value)
        {
            if (value is not BsonArray array) return [];
            var entries = new List<BlocksEventCatalogEntry>(array.Count);
            foreach (var element in array)
            {
                if (TryReadEntry(element, out var entry)) entries.Add(entry);
            }
            return entries;
        }

        private static bool TryReadEntry(BsonValue element, out BlocksEventCatalogEntry entry)
        {
            entry = null!;
            if (element is not BsonDocument document
                || !document.TryGetValue(nameof(BlocksEventCatalogEntry.EventName), out var eventName)
                || !eventName.IsString
                || string.IsNullOrWhiteSpace(eventName.AsString)
                || !IsBoolOrMissing(document, nameof(BlocksEventCatalogEntry.IsValidForWorkflow))
                || !IsBoolOrMissing(document, nameof(BlocksEventCatalogEntry.IsValidForFunction)))
            {
                return false;
            }

            try
            {
                entry = BsonSerializer.Deserialize<BlocksEventCatalogEntry>(document);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsBoolOrMissing(BsonDocument document, string name) =>
            !document.TryGetValue(name, out var value) || value.IsBoolean;
    }
}
