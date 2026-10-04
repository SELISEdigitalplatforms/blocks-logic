using Scheduler.DomainService.Entities;

namespace Scheduler.DomainService.Models
{
    public class ScheduledEventCommand : BaseScheduleCommand
    {
        public string Payload { get; set; }

        public ScheduledEventCommand PrepareScheduleCommand(Schedule schedule)
        {
            var scheduleCommand = Prepare<ScheduledEventCommand>(schedule);

            scheduleCommand.Payload = schedule.Payload;

            return scheduleCommand;
        }
    }
}
