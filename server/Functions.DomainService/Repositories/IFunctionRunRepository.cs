using Functions.DomainService.Entities;
using Functions.DomainService.Enums;

namespace Functions.DomainService.Repositories
{
    /// <summary>Optional filters for listing runs; a null field means "no filter".</summary>
    /// <param name="IdPrefix">Anchored prefix of a run's ItemId — the runs list searches by id.</param>
    public sealed record FunctionRunFilter(
        string? FunctionId,
        RunStatus? Status,
        DateTime? FromUtc,
        DateTime? ToUtc,
        InvokedByType? InvokedBy = null,
        string? IdPrefix = null);

    public interface IFunctionRunRepository
    {
        Task<(IReadOnlyList<FunctionRunEntity> Items, long TotalCount)> GetAllAsync(
            string tenantId, FunctionRunFilter filter, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Version ids of this function's runs that have not reached a terminal status — the
        /// versions retention must not delete, because work is still executing or retrying
        /// against them.
        /// </summary>
        Task<IReadOnlySet<string>> GetVersionIdsWithActiveRunsAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task<FunctionRunEntity?> GetByIdAsync(string tenantId, string runId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Run counts per version number for one function, for the versions tab's "Runs" column.
        /// One aggregation for the whole page rather than a count per row.
        /// </summary>
        Task<IReadOnlyDictionary<int, long>> CountByVersionAsync(
            string tenantId, string functionId, IReadOnlyCollection<int> versionNumbers,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Run counts since <paramref name="since"/> per function, for the list's "Runs 24 h"
        /// column. One aggregation for the whole page rather than a count per row.
        /// </summary>
        Task<IReadOnlyDictionary<string, long>> CountSinceByFunctionAsync(
            string tenantId, IReadOnlyCollection<string> functionIds, DateTime since,
            CancellationToken cancellationToken = default);

        Task CreateAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken = default);


        /// <summary>
        /// Applies the runner's terminal report to a run: status, error, timing, and metrics.
        /// A targeted update rather than a full replace, so it can never clobber a field the
        /// consumer does not know about (the run's input, its stage history, and so on).
        /// </summary>
        Task<bool> ApplyResultAsync(
            string tenantId,
            string runId,
            int attempt,
            RunStatus status,
            RunErrorCode errorCode,
            string? errorMessage,
            string? result,
            int? exitCode,
            long? durationMs,
            long? peakMemoryBytes,
            string? runnerId,
            DateTime? startedAt,
            DateTime completedAt,
            bool logsTruncated,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets a run for another attempt: status back to <c>Queued</c>, the attempt counter
        /// advanced, and the previous attempt's terminal fields cleared so a stale error or
        /// result from attempt N cannot linger on screen while attempt N+1 is in flight. The
        /// history in <c>Attempts</c> is untouched — that is the record of every attempt, not
        /// just the latest.
        /// </summary>
        Task<bool> ResetForRetryAsync(string tenantId, string runId, int attempt, CancellationToken cancellationToken = default);

        /// <summary>Flips only the status — used for the transient OUTPUT_PROCESSING step between a successful run and output delivery.</summary>
        Task ApplyStatusOnlyAsync(string tenantId, string runId, RunStatus status, CancellationToken cancellationToken = default);

        /// <summary>Records the output-action chain's outcome and the run's final status.</summary>
        Task ApplyOutputResultAsync(
            string tenantId, string runId, IReadOnlyList<Models.OutputActionResult> results,
            RunStatus finalStatus, string? errorMessage, CancellationToken cancellationToken = default);
    }
}
