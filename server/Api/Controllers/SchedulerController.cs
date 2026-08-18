using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Services;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class SchedulerController : ControllerBase
    {
        private readonly IScheduleService _scheduleService;

        public SchedulerController(IScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        /// <summary>
        /// Creates a schedule.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseMutationResponse> CreateSchedule([FromBody] CreateScheduleRequestDto request)
        {
            return await _scheduleService.CreateScheduleAsync(request);
        }

        /// <summary>
        /// Updates an existing schedule.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseMutationResponse> UpdateSchedule([FromBody] UpdateScheduleRequestDto request)
        {
            return await _scheduleService.UpdateScheduleAsync(request);
        }

        /// <summary>
        /// Permanently deletes a schedule.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> DeleteSchedule([FromBody] DeleteScheduleRequestDto request)
        {
            return await _scheduleService.DeleteScheduleAsync(request);
        }

        /// <summary>
        /// Get schedules.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<GetSchedulesResponseDto> GetSchedules([FromBody] GetSchedulesRequestDto request)
        {
            return await _scheduleService.GetSchedulesAsync(request);
        }
    }
}
