using Scheduler.DomainService.Entities;

namespace Scheduler.DomainService.Dtos
{
    public class SchedularDto
    {
        public List<Schedule> Schedules { get; set; }
        public string TenantId { get; set; }
    }
}
