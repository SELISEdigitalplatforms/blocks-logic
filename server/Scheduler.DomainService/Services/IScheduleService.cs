using Blocks.Genesis;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;

namespace Scheduler.DomainService.Services
{
    public interface IScheduleService
    {
        Task<BaseResponse> CreateScheduleAsync(CreateScheduleRequestDto request);
        Task<BaseResponse> UpdateScheduleAsync(UpdateScheduleRequestDto request);
        Task<BaseResponse> DeleteScheduleAsync(DeleteScheduleRequestDto request);
        Task<GetSchedulesResponseDto> GetSchedulesAsync(GetSchedulesRequestDto request);
    }
}
