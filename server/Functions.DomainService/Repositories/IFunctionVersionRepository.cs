using Functions.DomainService.Entities;

namespace Functions.DomainService.Repositories
{
    /// <summary>
    /// Versions are immutable once created (DECISIONS.md): nothing here ever updates a
    /// version's content, only reads it or creates the next one.
    /// </summary>
    public interface IFunctionVersionRepository
    {
        Task<(IReadOnlyList<FunctionVersionEntity> Items, long TotalCount)> GetAllAsync(
            string tenantId, string functionId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default);

        Task<FunctionVersionEntity?> GetByIdAsync(string tenantId, string versionId, CancellationToken cancellationToken = default);

        Task<FunctionVersionEntity?> GetByNumberAsync(string tenantId, string functionId, int number, CancellationToken cancellationToken = default);

        /// <summary>The most recently created version, or null if none exist yet.</summary>
        Task<FunctionVersionEntity?> GetLatestAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task CreateAsync(string tenantId, FunctionVersionEntity version, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every version of one function, newest first, without the source snapshots — enough to
        /// decide what retention should keep.
        /// </summary>
        Task<IReadOnlyList<FunctionVersionEntity>> GetAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task<long> DeleteManyAsync(
            string tenantId, IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken = default);

        Task<long> DeleteAllForFunctionAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);
    }
}
