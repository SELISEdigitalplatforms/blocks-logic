using System.ComponentModel.DataAnnotations;

namespace Scheduler.DomainService.Dtos.Requests
{
    /// <summary>One execution row of a schedule, in full (the details sheet).</summary>
    public class GetScheduleExecutionRequestDto
    {
        [Required]
        [StringLength(64)]
        public string ScheduleId { get; set; } = string.Empty;

        [Required]
        [StringLength(64)]
        public string ExecutionId { get; set; } = string.Empty;
    }
}
