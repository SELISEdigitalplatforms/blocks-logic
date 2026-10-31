using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;

namespace Scheduler.DomainService.Services
{
    /// <summary>Read side of the schedule execution history (Application schedules only).</summary>
    public interface IScheduleExecutionQueryService
    {
        Task<GetScheduleExecutionsResponseDto> GetScheduleExecutionsAsync(GetScheduleExecutionsRequestDto request);

        Task<GetScheduleExecutionResponseDto> GetScheduleExecutionAsync(GetScheduleExecutionRequestDto request);
    }
}
