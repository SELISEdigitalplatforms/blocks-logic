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
public sealed record ReadScope<T>(bool Granted, FilterDefinition<T> Filter, ProjectionDefinition<T>? Projection)
{
    public IFindFluent<T, T> Apply(IMongoCollection<T> collection)
    {
        var find = collection.Find(Filter);
        return Projection is null ? find : find.Project<T>(Projection);
    }
}

/// <summary>
/// The one door between logic and the Genesis data access resolver (row filter + projection only).
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
}
