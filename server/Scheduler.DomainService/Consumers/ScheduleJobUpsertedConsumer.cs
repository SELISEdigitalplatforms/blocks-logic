using Blocks.Genesis;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Services;

namespace Scheduler.DomainService.Consumers
{
    public class ScheduleJobUpsertedConsumer : IConsumer<ScheduleJobUpsertedEvent>
    {
        private readonly IScheduleJobRegistry _scheduleJobRegistry;

        public ScheduleJobUpsertedConsumer(IScheduleJobRegistry scheduleJobRegistry)
        {
            _scheduleJobRegistry = scheduleJobRegistry;
        }

        public async Task Consume(ScheduleJobUpsertedEvent @event)
        {
            await _scheduleJobRegistry.ApplyUpsertAsync(@event);
        }
    }
}
