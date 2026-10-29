using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Services
{
    /// <summary>
    /// Writes one schedule execution row after delivery has finished. Best-effort: never throws, so recording can
    /// never fail or retry a delivery.
    /// </summary>
    public interface IScheduleExecutionRecorder
    {
        Task RecordAsync(ScheduleExecution execution);
    }

    /// <summary>Records nothing. The default when no recorder is registered.</summary>
    public sealed class NullScheduleExecutionRecorder : IScheduleExecutionRecorder
    {
        public static readonly NullScheduleExecutionRecorder Instance = new();

        public Task RecordAsync(ScheduleExecution execution) => Task.CompletedTask;
    }
}
