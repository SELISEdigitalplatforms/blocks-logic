using Functions.DomainService.Entities;
using Functions.DomainService.Enums;

namespace Functions.DomainService.Repositories
{
    /// <summary>Optional filters for listing runs; a null field means "no filter".</summary>
    /// <param name="IdPrefix">Anchored prefix of a run's ItemId — the runs list searches by id.</param>
    /// <summary>
    /// <paramref name="ActiveOnly"/> is the design's "Running" chip: it covers every non-terminal
    /// status, not the single <see cref="RunStatus.Running"/> one. It belongs here rather than in
    /// the client, which used to ask for the whole page and hide the terminal rows itself — so the
    /// page size, the total count and the pager all described a different set than the table did.
    /// </summary>
    public sealed record FunctionRunFilter(
        string? FunctionId,
        RunStatus? Status,
        DateTime? FromUtc,
        DateTime? ToUtc,
        InvokedByType? InvokedBy = null,
        string? IdPrefix = null,
        bool ActiveOnly = false);

    /// <summary>What <see cref="IFunctionRunRepository.ApplyResultAsync"/> did with a result.</summary>
    public enum ApplyResultOutcome
    {
        /// <summary>The result was written.</summary>
        Applied = 0,

        /// <summary>No run with that id exists in the tenant.</summary>
        NotFound = 1,

        /// <summary>The run already holds this attempt's outcome; nothing was written.</summary>
        Duplicate = 2,

        /// <summary>The result is for an attempt older than the run's current one; nothing was written.</summary>
        StaleAttempt = 3,

        /// <summary>The result names an attempt newer than any the run has started; nothing was written.</summary>
        UnknownAttempt = 4,
    }

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

        /// <summary>Removes every run of one function. Returns how many went.</summary>
        Task<long> DeleteAllForFunctionAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        Task CreateAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken = default);


        /// <summary>
        /// Applies the runner's terminal report to a run: status, error, timing, and metrics.
        /// A targeted update rather than a full replace, so it can never clobber a field the
        /// consumer does not know about (the run's input, its stage history, and so on).
        /// <para>
        /// <b>Guarded, in the same atomic update.</b> The write only lands when the record's
        /// current <c>Attempt</c> is <paramref name="attempt"/> and the record is still waiting for
        /// that attempt's outcome — non-terminal (and not already past the result, as
        /// <c>OUTPUT_PROCESSING</c> is), or closed by the platform itself
        /// (<see cref="RunErrorCode.Undeliverable"/>, <see cref="RunErrorCode.EnqueueFailed"/>,
        /// <see cref="RunErrorCode.Abandoned"/>), which a real outcome is allowed to replace. So a
        /// redelivered result is a no-op, and a late result from an older attempt can never
        /// overwrite the outcome of a newer one. The attempt recorded in <c>Attempts</c> is the
        /// one the result reports.
        /// </para>
        /// </summary>
        /// <returns>What happened; anything other than <see cref="ApplyResultOutcome.Applied"/> wrote nothing.</returns>
        Task<ApplyResultOutcome> ApplyResultAsync(
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
            long? cpuUsageMs,
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
        /// <para>
        /// Conditional: only a run whose current attempt is <c><paramref name="attempt"/> - 1</c>
        /// and which is terminal is reset, so two Worker instances racing the same due retry, or a
        /// stale retry entry, advance it at most once.
        /// </para>
        /// </summary>
        /// <returns>True when this call is what advanced the run.</returns>
        Task<bool> ResetForRetryAsync(string tenantId, string runId, int attempt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fails a run that never ran, but only while it is still non-terminal.
        /// <para>
        /// This is the dead-letter path (<see cref="Consumers.FunctionDeadLetterConsumer"/>) and
        /// the condition is the whole point of it being its own method. A dead-lettered entry
        /// says "no runner completed this", which is usually true and occasionally stale: a
        /// runner can publish its result and be killed before acknowledging, leaving the entry to
        /// be reclaimed, re-delivered past its budget and dead-lettered while a perfectly good
        /// SUCCEEDED result is already in Mongo. Filtering on the status in the same update makes
        /// that race a no-op instead of overwriting a real outcome with a false failure.
        /// </para>
        /// </summary>
        /// <returns>True when this call is what marked the run failed.</returns>
        Task<bool> FailIfNotTerminalAsync(
            string tenantId,
            string runId,
            RunErrorCode errorCode,
            string errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default);

        /// <summary>Flips only the status — used for the transient OUTPUT_PROCESSING step between a successful run and output delivery.</summary>
        Task ApplyStatusOnlyAsync(string tenantId, string runId, RunStatus status, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves a <c>SUCCEEDED</c> run of <paramref name="attempt"/> that has no output results
        /// yet to <c>OUTPUT_PROCESSING</c>, atomically. False when the run is anywhere else — the
        /// hand-off already happened, the output chain already finished, or a newer attempt exists.
        /// </summary>
        Task<bool> TryBeginOutputProcessingAsync(
            string tenantId, string runId, int attempt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records the output-action chain's outcome and the run's final status — only while the
        /// run is still <c>OUTPUT_PROCESSING</c> for <paramref name="attempt"/>, so a duplicate
        /// output job, or one that lost a race with the stale-run sweeper, cannot rewrite a
        /// verdict already given.
        /// </summary>
        /// <returns>True when this call wrote the verdict.</returns>
        Task<bool> ApplyOutputResultAsync(
            string tenantId, string runId, int attempt, IReadOnlyList<Models.OutputActionResult> results,
            RunStatus finalStatus, string? errorMessage, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs in one of <paramref name="statuses"/> whose <c>LastUpdatedDate</c> is before
        /// <paramref name="updatedBefore"/> (and not before <paramref name="updatedNotBefore"/>,
        /// when given), oldest first, at most <paramref name="limit"/>. Input and Result are not
        /// fetched. The candidates for <c>FunctionStaleRunSweeper</c>.
        /// </summary>
        Task<IReadOnlyList<FunctionRunEntity>> FindStaleCandidatesAsync(
            string tenantId, IReadOnlyCollection<RunStatus> statuses, DateTime updatedBefore,
            DateTime? updatedNotBefore, int limit, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes a run the sweeper judged stale — a compare-and-set on exactly what the sweeper
        /// read: the same status, attempt and <c>LastUpdatedDate</c>. Any change in between (a
        /// real result, a retry reset, output delivery finishing) makes this a no-op, so the
        /// sweeper can never overwrite an outcome that raced in.
        /// </summary>
        /// <returns>True when this call closed the run.</returns>
        Task<bool> CloseStaleAsync(
            string tenantId, string runId, RunStatus expectedStatus, int expectedAttempt, DateTime expectedLastUpdated,
            RunStatus newStatus, RunErrorCode errorCode, string errorMessage, DateTime completedAt,
            CancellationToken cancellationToken = default);
    }
}
