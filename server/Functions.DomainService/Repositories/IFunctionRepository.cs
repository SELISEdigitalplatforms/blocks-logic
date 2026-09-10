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
            string tenantId, string? searchKey, string? status, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default);

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

        Task<bool> DeleteAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

    }
}
