using Functions.DomainService.Entities;

namespace Functions.DomainService.Repositories
{
    public interface IFunctionRunLogRepository
    {
        /// <summary>Bulk-inserts one run's log lines in the order they were emitted.</summary>
        Task InsertManyAsync(string tenantId, IReadOnlyList<FunctionRunLogEntity> logs, CancellationToken cancellationToken = default);

        /// <summary>Removes every log line of every run of one function. Returns how many went.</summary>
        Task<long> DeleteAllForFunctionAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<FunctionRunLogEntity> Items, long TotalCount)> GetByRunAsync(
            string tenantId, string runId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    }
}
