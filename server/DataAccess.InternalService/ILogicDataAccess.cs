using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DataAccess.InternalService;

/// <summary>Schema names of the Genesis system entities that logic owns.</summary>
public static class LogicSchemas
{
    public const string WorkflowEntity = "WorkflowEntity";
    public const string WorkflowVersionEntity = "WorkflowVersionEntity";
    public const string WorkflowExecutionEntity = "WorkflowExecutionEntity";
    public const string WorkflowItemExecutionEntity = "WorkflowItemExecutionEntity";
    public const string FunctionEntity = "FunctionEntity";
    public const string FunctionVersionEntity = "FunctionVersionEntity";
    public const string FunctionRunEntity = "FunctionRunEntity";
    public const string FunctionRunLogEntity = "FunctionRunLogEntity";
    public const string FunctionBuildEntity = "FunctionBuildEntity";
    public const string FunctionAuditEventEntity = "FunctionAuditEventEntity";
    public const string ProxyDetailEntity = "ProxyDetailEntity";
    public const string ProxyVersionEntity = "ProxyVersionEntity";
    public const string ProxyExecutionEntity = "ProxyExecutionEntity";
    public const string Schedule = "Schedule";
    public const string MailBoxEntity = "MailBoxEntity";
    public const string MailBoxSyncCursor = "MailBoxSyncCursor";
    public const string NotificationConnection = "NotificationConnection";
    public const string NotificationSubscription = "NotificationSubscription";
    public const string OfflineNotification = "OfflineNotification";
    public const string FirebaseConfiguration = "FirebaseConfiguration";
    public const string StorageConfiguration = "StorageConfiguration";
}

/// <summary>
/// A resolved read. <see cref="Granted"/> false means "treat as not found / empty".
/// <see cref="Apply"/> builds the Find with the resolver's filter and projection.
/// </summary>
public sealed record ReadScope<T>(bool Granted, FilterDefinition<T> Filter, ProjectionDefinition<T>? Projection,
    Func<Dictionary<string, object>, Dictionary<string, object>>? RowMask = null)
{
    public IFindFluent<T, T> Apply(IMongoCollection<T> collection)
    {
        var find = collection.Find(Filter);
        return Projection is null ? find : find.Project<T>(Projection);
    }

    /// <summary>
    /// For what goes back to the API caller: properties a View column rule hides from them are cleared (a
    /// Genesis column mask, as the Data Gateway applies). Never save a masked entity back: write paths read the
    /// whole document instead. No-op on the skip path and where no column rule applies.
    /// </summary>
    public T ForView(T entity) => RowMask is null || entity is null ? entity : ColumnMask.Apply(entity, RowMask);

    /// <inheritdoc cref="ForView(T)"/>
    public List<T> ForView(List<T> entities) => RowMask is null ? entities : entities.ConvertAll(ForView);
}

/// <summary>
/// The one door between logic and the Genesis data access resolver (row filter + projection; column
/// masking only on reads that opt in with ReadScope.ForView).
/// Every call on Repos, Builds and RepositoryWebhooks goes through it. Pass the tenant DB the call
/// targets in <c>tenantId</c> (null = the caller's own DB).
/// </summary>
public interface ILogicDataAccess
{
    /// <summary>Row filter and projection for a read. Denied: <see cref="ReadScope{T}.Granted"/> is false.</summary>
    Task<ReadScope<T>> ReadAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null, bool useProjection = true);

    /// <summary>Filter for an update or replace (EDIT). Null = denied, do not write.</summary>
    Task<FilterDefinition<T>?> EditFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>Filter for a delete (DELETE). Null = denied, do not write.</summary>
    Task<FilterDefinition<T>?> DeleteFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>WRITE gate for an insert. Input fields are never dropped. False = denied.</summary>
    Task<bool> InsertAllowedAsync(string schema, string callSite, string? tenantId = null);

    /// <summary>
    /// Default values and validation rules for an insert or a whole-document replace. On insert, defaults
    /// fill the entity's null properties, in the instance passed in (which is also returned). Schemas the
    /// Genesis allow-list keeps closed skip them. Throws <see cref="Blocks.Genesis.DataAccess.Rules.DataValidationException"/> when a rule fails.
    /// On insert the requester's user id is then added to the record's Read, Update and Delete UserIds.
    /// </summary>
    Task<T> PrepareWriteAsync<T>(string schema, T entity, bool isInsert, string callSite, string? tenantId = null);

    /// <summary>
    /// As <see cref="PrepareWriteAsync{T}"/> for an upsert: it is an insert when nothing matches
    /// <paramref name="filter"/> in <paramref name="collection"/>.
    /// </summary>
    Task<T> PrepareUpsertAsync<T>(string schema, T entity, IMongoCollection<T> collection, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>
    /// For a whole-document replace of what <paramref name="filter"/> matches: as <see cref="PrepareWriteAsync{T}"/>
    /// (not an insert), after putting back the stored values of properties a View column rule hides from the caller
    /// when the replacement carries what their masked read showed (PrepareUpsertAsync does the same on update).
    /// </summary>
    Task<T> PrepareReplaceAsync<T>(string schema, T entity, IMongoCollection<T> collection, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>
    /// For a partial update built from what a client sent back (an edit form): drops each <c>$set</c> of a property
    /// a View column rule hides from the caller when it carries exactly what their masked read showed, so saving
    /// what was read never erases the stored value. Null when nothing is left to write. Not for server-decided values.
    /// </summary>
    Task<UpdateDefinition<T>?> KeepHiddenAsync<T>(string schema, UpdateDefinition<T> update, IMongoCollection<T> collection, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>
    /// Validation rules for a partial update: only the <c>$set</c> fields are checked, the same "only what is
    /// sent" rule as the Data Gateway; defaults never apply. Throws
    /// <see cref="Blocks.Genesis.DataAccess.Rules.DataValidationException"/> when a rule fails.
    /// </summary>
    Task CheckUpdateAsync<T>(string schema, UpdateDefinition<T> update, string callSite, string? tenantId = null);
}

/// <summary>Applies a Genesis column mask (built for rows) to a typed entity.</summary>
internal static class ColumnMask
{
    public static T Apply<T>(T entity, Func<Dictionary<string, object>, Dictionary<string, object>> mask)
    {
        var document = entity.ToBsonDocument();
        var row = mask(document.ToDictionary());
        return RemoveHidden(document, row) ? BsonSerializer.Deserialize<T>(document) : entity;
    }

    /// <summary>
    /// For a whole-document update: puts back the stored value of every property the caller cannot view (a View
    /// column rule hides it) when the update carries exactly what the masked read showed them, so a client that
    /// saves what it read never erases it. A different value the caller sent is kept.
    /// </summary>
    public static T KeepHidden<T>(T incoming, T stored, Func<Dictionary<string, object>, Dictionary<string, object>> mask)
    {
        var storedDocument = stored.ToBsonDocument();
        var view = storedDocument.DeepClone().AsBsonDocument;
        if (!RemoveHidden(view, mask(storedDocument.ToDictionary())))
            return incoming;
        var shown = BsonSerializer.Deserialize<T>(view).ToBsonDocument();
        var update = incoming.ToBsonDocument();
        return Restore(update, storedDocument, view, shown) ? BsonSerializer.Deserialize<T>(update) : incoming;
    }

    /// <summary>
    /// For a partial update built from what a client sent back: the <c>$set</c> entries that write a property the
    /// caller cannot view with exactly what their masked read showed (null or the default), which would erase the
    /// stored value. Null when there are none.
    /// </summary>
    public static HashSet<string>? HiddenSetPaths<T>(BsonDocument set, T stored, Func<Dictionary<string, object>, Dictionary<string, object>> mask)
    {
        var storedDocument = stored.ToBsonDocument();
        var view = storedDocument.DeepClone().AsBsonDocument;
        if (!RemoveHidden(view, mask(storedDocument.ToDictionary())))
            return null;
        var shown = BsonSerializer.Deserialize<T>(view).ToBsonDocument();
        var paths = set.Names
            .Where(path => IsHidden(path, storedDocument, view)
                           && (set[path].IsBsonNull || set[path].Equals(ValueAt(shown, path))))
            .ToHashSet();
        return paths.Count > 0 ? paths : null;
    }

    /// <summary>The mask took the property at this dotted path out of <paramref name="view"/> (it held a value).</summary>
    private static bool IsHidden(string path, BsonDocument stored, BsonDocument view)
    {
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (!stored.TryGetValue(parts[i], out var storedValue) || storedValue.IsBsonNull)
                return false;
            if (!view.TryGetValue(parts[i], out var viewValue))
                return true;
            if (i == parts.Length - 1 || storedValue is not BsonDocument nextStored || viewValue is not BsonDocument nextView)
                return false;
            stored = nextStored;
            view = nextView;
        }
        return false;
    }

    private static BsonValue ValueAt(BsonDocument document, string path)
    {
        BsonValue current = document;
        foreach (var part in path.Split('.'))
        {
            if (current is not BsonDocument nested || !nested.TryGetValue(part, out current))
                return BsonNull.Value;
        }
        return current;
    }

    /// <summary>Puts back, property by property, what the mask took out of <paramref name="view"/>. True when anything was.</summary>
    private static bool Restore(BsonDocument update, BsonDocument stored, BsonDocument view, BsonDocument shown)
    {
        var changed = false;
        foreach (var element in stored)
        {
            if (element.Value.IsBsonNull)
                continue;
            if (!view.TryGetValue(element.Name, out var viewValue))
            {
                var sent = update.GetValue(element.Name, BsonNull.Value);
                if (sent.IsBsonNull || sent.Equals(shown.GetValue(element.Name, BsonNull.Value)))
                {
                    update[element.Name] = element.Value;
                    changed = true;
                }
            }
            else if (element.Value is BsonDocument nested && viewValue is BsonDocument nestedView
                     && update.GetValue(element.Name, BsonNull.Value) is BsonDocument nestedUpdate
                     && shown.GetValue(element.Name, BsonNull.Value) is BsonDocument nestedShown)
            {
                changed |= Restore(nestedUpdate, nested, nestedView, nestedShown);
            }
        }
        return changed;
    }

    /// <summary>
    /// Takes out of <paramref name="document"/> what the mask cleared in <paramref name="row"/> (set to null or
    /// removed), so a hidden property reads as unset; every other value stays exactly as stored. True when anything was.
    /// </summary>
    private static bool RemoveHidden(BsonDocument document, IDictionary<string, object> row)
    {
        var changed = false;
        // A snapshot: properties are removed from the document while this runs.
        foreach (var element in document.Where(element => !element.Value.IsBsonNull).ToList())
            changed |= RemoveHiddenProperty(document, element, row);
        return changed;
    }

    /// <summary>
    /// One property of <see cref="RemoveHidden"/>: taken out when the mask cleared it; otherwise its nested documents,
    /// or the documents of its array, are checked the same way.
    /// </summary>
    private static bool RemoveHiddenProperty(BsonDocument document, BsonElement element, IDictionary<string, object> row)
    {
        if (!row.TryGetValue(element.Name, out var masked) || masked is null)
        {
            document.Remove(element.Name);
            return true;
        }
        return (element.Value, masked) switch
        {
            (BsonDocument nested, IDictionary<string, object> nestedRow) => RemoveHidden(nested, nestedRow),
            (BsonArray array, System.Collections.IList items) when items.Count == array.Count => RemoveHiddenInArray(array, items),
            _ => false,
        };
    }

    /// <summary><see cref="RemoveHidden"/> for each document of an array, paired by position with the masked items.</summary>
    private static bool RemoveHiddenInArray(BsonArray array, System.Collections.IList items)
    {
        var changed = false;
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is BsonDocument item && items[i] is IDictionary<string, object> itemRow)
                changed |= RemoveHidden(item, itemRow);
        }
        return changed;
    }
}
