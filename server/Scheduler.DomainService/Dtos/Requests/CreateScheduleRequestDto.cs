using Scheduler.DomainService.Models;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Dtos.Requests
{
    public class CreateScheduleRequestDto
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public string Payload { get; set; }
        public string CronExpression { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public WebhookConfiguration Webhook { get; set; }

        /// <summary>IANA time zone id (e.g. "Asia/Dhaka"). Null, empty, "UTC" or "Etc/UTC" = UTC.
        /// Update replaces the whole schedule: omitting it on update stores UTC.</summary>
        public string? TimeZone { get; set; }
    }
}
