using Scheduler.DomainService.Models;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Dtos.Responses
{
    public class ScheduleDto
    {
        public string ItemId { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Payload { get; set; }
        public string CronExpression { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public ScheduleKind Kind { get; set; } = ScheduleKind.Application;
        public ScheduleTriggerType TriggerType { get; set; } = ScheduleTriggerType.Queue;
        public WebhookConfiguration? Webhook { get; set; }
        public QueueConfiguration? Queue { get; set; }

        /// <summary>The stored IANA zone; null = UTC.</summary>
        public string? TimeZone { get; set; }

        /// <summary>The next fire (UTC) within the active window; null when inactive or none is due.</summary>
        public DateTime? NextRunAtUtc { get; set; }
    }
}
