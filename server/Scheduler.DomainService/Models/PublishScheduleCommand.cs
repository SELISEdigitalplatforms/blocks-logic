using Scheduler.DomainService.Entities;

namespace Scheduler.DomainService.Models
{
    public class PublishScheduleCommand : BaseScheduleCommand
    {
        public object Payload { get; set; }

        public PublishScheduleCommand Build(Schedule schedule)
        {
            var scheduleCommand = Prepare<PublishScheduleCommand>(schedule);

            scheduleCommand.Payload = schedule.Payload;

            return scheduleCommand;
        }
    }
}
