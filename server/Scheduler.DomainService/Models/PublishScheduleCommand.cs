using Scheduler.DomainService.Entities;

namespace Scheduler.DomainService.Models
{
    public class PublishScheduleCommand : BaseScheduleCommand
    {
        /// <summary>
        /// The schedule payload as a raw JSON string. Typed as string (not object) so the
        /// payload is carried as a single JSON-encoded value on the wire; consumers deserialize
        /// it directly instead of receiving a double-encoded string element.
        /// </summary>
        public string Payload { get; set; } = string.Empty;

        public PublishScheduleCommand Build(Schedule schedule)
        {
            var scheduleCommand = Prepare<PublishScheduleCommand>(schedule);

            scheduleCommand.Payload = schedule.Payload;

            return scheduleCommand;
        }
    }
}
