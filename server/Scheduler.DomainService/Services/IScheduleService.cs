using Blocks.Genesis;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;

namespace Scheduler.DomainService.Services
{
    public interface IScheduleService
    {
        Task<BaseMutationResponse> CreateScheduleAsync(CreateScheduleRequestDto request);
        Task<BaseMutationResponse> UpdateScheduleAsync(UpdateScheduleRequestDto request);
        Task<BaseResponse> DeleteScheduleAsync(DeleteScheduleRequestDto request);
        Task<BaseMutationResponse> CreateWorkflowScheduleAsync(CreateWorkflowScheduleRequest request);
        Task<BaseResponse> DeleteWorkflowSchedulesAsync(IEnumerable<string> itemIds);
        Task<GetSchedulesResponseDto> GetSchedulesAsync(GetSchedulesRequestDto request);
    }
}
