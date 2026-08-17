using Scheduler.DomainService.Models;

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
    }
}
