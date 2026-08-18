using Scheduler.DomainService.Models;

namespace Scheduler.DomainService.Dtos.Requests
{
    public class UpdateScheduleRequestDto : CreateScheduleRequestDto
    {
        public string ItemId { get; set; }
        public bool IsActive { get; set; }
    }
}
