using Functions.DomainService.Entities;
using Functions.DomainService.Models;

namespace Functions.DomainService.Repositories
{
    /// <summary>
    /// Every method takes <c>tenantId</c> explicitly rather than relying on the ambient
    /// <c>BlocksContext</c>. The Worker background services process results for whichever
    /// tenant the runner happens to echo back next, with no HTTP request and no ambient
    /// ASP.NET context to fall back on — an implicit-tenant repository would work by accident
    /// in the Api and silently misbehave in the Worker.
    /// </summary>
    public interface IFunctionRepository
    {
        Task<(IReadOnlyList<FunctionEntity> Items, long TotalCount)> GetAllAsync(
            string tenantId, string? searchKey, string? status, string? sortBy,
            int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        Task<FunctionEntity?> GetByIdAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task CreateAsync(string tenantId, FunctionEntity function, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames a function. Field-level, like every write below: a whole-document replace
        /// would carry a copy loaded earlier and silently undo anything written in between —
        /// which is exactly how the run counters used to be lost.
        /// </summary>
        Task UpdateDetailsAsync(
            string tenantId, string functionId, string name, string? description, string? actorId,
            CancellationToken cancellationToken = default);

        /// <summary>Saves the editor's working copy: source plus configuration, nothing else.</summary>
        Task UpdateSourceAndConfigAsync(
            string tenantId, string functionId, FunctionSource source, string sourceHash,
            FunctionLimits limits, RetryPolicy retry, TriggerConfig trigger,
            List<OutputAction> outputActions, List<VariableBinding> variables, string? actorId,
            CancellationToken cancellationToken = default);

        /// <summary>Points the function at a newly created version and marks it Live.</summary>
        Task SetActiveVersionAsync(
            string tenantId, string functionId, string versionId, int versionNumber, DateTime deployedAt,
            string? actorId, CancellationToken cancellationToken = default);

        /// <summary>Rollback: moves the active-version pointer and nothing else.</summary>
        Task MoveActiveVersionAsync(
            string tenantId, string functionId, string versionId, string? actorId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Accepts a delete: stamps the tombstone, after which every read and write here treats
        /// the function as gone. False when it is already gone or already being deleted, so two
        /// concurrent deletes produce one tombstone and one audit record.
        /// </summary>
        /// <summary>
        /// Counts one started run: <c>$inc TotalRuns</c>, <c>$max LastRunAt</c>, in one write with
        /// no read first, so concurrent runs cannot lose each other's increments. Leaves
        /// <c>LastUpdatedDate</c> alone — a run is not an edit, and the list sorts by it. Never
        /// throws: a counter is not worth failing an invocation over.
        /// </summary>
        Task RecordRunStartedAsync(
            string tenantId, string functionId, DateTime startedAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds counts carried over from the retired <c>FunctionRunStats</c> collection. Additive,
        /// so runs counted on the function while the carry-over runs are kept.
        /// </summary>
        Task ImportRunCountersAsync(
            string tenantId, string functionId, long totalRuns, DateTime? lastRunAt,
            CancellationToken cancellationToken = default);

        Task<bool> MarkDeletedAsync(
            string tenantId, string functionId, FunctionDeletion deletion, CancellationToken cancellationToken = default);

        /// <summary>A tombstoned function (without its source), or null when there is none.</summary>
        Task<FunctionEntity?> GetDeletedAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>Ids of tombstoned functions, oldest delete first — the backstop sweep's input.</summary>
        Task<IReadOnlyList<string>> GetDeletedIdsAsync(string tenantId, int limit, CancellationToken cancellationToken = default);

        /// <summary>Adds one purge pass's counts to the tombstone.</summary>
        Task RecordPurgePassAsync(
            string tenantId, string functionId, Services.FunctionPurgeReport report, DateTime at,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the document for good — only ever a tombstoned one, so no code path can remove
        /// a live function without the purge having run first.
        /// </summary>
        Task<bool> DeleteTombstoneAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

    }
}
