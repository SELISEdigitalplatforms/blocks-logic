using Scheduler.DomainService.Events;

namespace Scheduler.DomainService.Services
{
    public interface IScheduleJobRegistry
    {
        Task ApplyUpsertAsync(ScheduleJobUpsertedEvent @event);
        Task ApplyDeleteAsync(ScheduleJobDeletedEvent @event);
        Task RegisterAllAsync();
    }
}
