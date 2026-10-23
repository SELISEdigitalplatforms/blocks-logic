using Functions.DomainService.Events;

namespace Functions.DomainService.Repositories
{
    /// <summary>
    /// The events a function may send (<see cref="FunctionEventSettingsEntity"/>): the draft the editor changes and a
    /// copy per deployed version. Every method takes the tenant explicitly, like the other Functions repositories.
    /// </summary>
    public interface IFunctionEventSettingsRepository
    {
        /// <summary>The row of a version, or of the draft when <paramref name="versionId"/> is null or empty. Null when there is none.</summary>
        Task<FunctionEventSettingsEntity?> GetAsync(
            string tenantId, string functionId, string? versionId, CancellationToken cancellationToken = default);

        /// <summary>Writes the draft row (replacing the whole row; there is one per function).</summary>
        Task SaveDraftAsync(
            string tenantId, string functionId, List<FunctionEventSetting> events, List<string> triggerEvents,
            string? actorId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deploy: copies the draft row to one for the new version. With no draft row there is nothing to copy and
        /// no row is written (a version without a row sends no events). Repeating it overwrites the same row.
        /// </summary>
        Task CopyDraftToVersionAsync(
            string tenantId, string functionId, string versionId, CancellationToken cancellationToken = default);

        /// <summary>Removes the rows of versions that were deleted. Returns how many went.</summary>
        Task<long> DeleteVersionsAsync(
            string tenantId, IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken = default);

        /// <summary>Removes every row of one function (draft and versions). Returns how many went.</summary>
        Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);
    }
}
