using Blocks.Genesis;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Services;

namespace Scheduler.DomainService.Consumers
{
    public class ScheduleJobDeletedConsumer : IConsumer<ScheduleJobDeletedEvent>
    {
        private readonly IScheduleJobRegistry _scheduleJobRegistry;

        public ScheduleJobDeletedConsumer(IScheduleJobRegistry scheduleJobRegistry)
        {
            _scheduleJobRegistry = scheduleJobRegistry;
        }

        public async Task Consume(ScheduleJobDeletedEvent @event)
        {
            await _scheduleJobRegistry.ApplyDeleteAsync(@event);
        }
    }
}
