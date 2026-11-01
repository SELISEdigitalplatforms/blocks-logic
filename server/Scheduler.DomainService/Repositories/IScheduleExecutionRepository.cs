using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Repositories
{
    /// <summary>
    /// What a history list or count matches. <paramref name="Statuses"/> is an <c>$in</c> equality list on
    /// <c>Response.Status</c> (null = no outcome filter); <paramref name="NonSuccessRange"/> is the fallback
    /// <c>Status &lt; 200 || Status &gt; 299</c> used when a schedule has too many distinct failure codes for an
    /// <c>$in</c>. <paramref name="StartUtc"/> is inclusive, <paramref name="EndUtc"/> exclusive, both on <c>FiredUtc</c>.
    /// </summary>
    public sealed record ScheduleExecutionFilter(
        string ScheduleId,
        IReadOnlyCollection<int>? Statuses = null,
        DateTime? StartUtc = null,
        DateTime? EndUtc = null,
        bool NonSuccessRange = false)
    {
        public bool HasOutcome => Statuses is not null || NonSuccessRange;
    }

    public interface IScheduleExecutionRepository
    {
        /// <summary>Inserts one row into the tenant's <c>ScheduleExecutions</c>; creates the indexes on first use.</summary>
        Task InsertAsync(ScheduleExecution execution, CancellationToken cancellationToken = default);

        /// <summary>Light rows (id, times, status), newest first, for the caller's DB.</summary>
        Task<List<ScheduleExecution>> GetPageAsync(ScheduleExecutionFilter filter, int pageNumber, int pageSize);

        /// <summary>Count with the same filter and hint as <see cref="GetPageAsync"/>.</summary>
        Task<long> CountAsync(ScheduleExecutionFilter filter);

        /// <summary>The full row, or null when it is missing or belongs to another schedule.</summary>
        Task<ScheduleExecution?> GetByIdAsync(string scheduleId, string executionId);

        /// <summary>The distinct integer <c>Response.Status</c> values stored for one schedule.</summary>
        Task<IReadOnlyList<int>> GetDistinctStatusesAsync(string scheduleId);
    }
}
