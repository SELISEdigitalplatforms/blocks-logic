using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Services;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>Scheduled-job management, routed as <c>/api/Scheduler/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class SchedulerController : ControllerBase
    {
        private readonly IScheduleService _scheduleService;
        private readonly IScheduleExecutionQueryService _executionQueryService;

        /// <summary>Takes the schedule service and the execution-history query service.</summary>
        public SchedulerController(IScheduleService scheduleService, IScheduleExecutionQueryService executionQueryService)
        {
            _scheduleService = scheduleService;
            _executionQueryService = executionQueryService;
        }

        /// <summary>
        /// Creates a schedule.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::scheduler::manage")]
        public async Task<BaseMutationResponse> CreateSchedule([FromBody] CreateScheduleRequestDto request)
        {
            return await _scheduleService.CreateScheduleAsync(request);
        }

        /// <summary>
        /// Updates an existing schedule.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::scheduler::manage")]
        public async Task<BaseMutationResponse> UpdateSchedule([FromBody] UpdateScheduleRequestDto request)
        {
            return await _scheduleService.UpdateScheduleAsync(request);
        }

        /// <summary>
        /// Permanently deletes a schedule.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::scheduler::manage")]
        public async Task<BaseResponse> DeleteSchedule([FromBody] DeleteScheduleRequestDto request)
        {
            return await _scheduleService.DeleteScheduleAsync(request);
        }

        /// <summary>
        /// Get schedules.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::scheduler::read")]
        public async Task<GetSchedulesResponseDto> GetSchedules([FromBody] GetSchedulesRequestDto request)
        {
            return await _scheduleService.GetSchedulesAsync(request);
        }

        /// <summary>
        /// One page of an Application schedule's execution history, newest first, optionally filtered by outcome
        /// and by fired date (<c>StartDate</c> inclusive, <c>EndDate</c> exclusive).
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::scheduler::read")]
        public async Task<GetScheduleExecutionsResponseDto> GetScheduleExecutions([FromBody] GetScheduleExecutionsRequestDto request)
        {
            return await _executionQueryService.GetScheduleExecutionsAsync(request);
        }

        /// <summary>
        /// One execution of an Application schedule, in full: status, body and the schedule snapshot.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::scheduler::read")]
        public async Task<GetScheduleExecutionResponseDto> GetScheduleExecution([FromBody] GetScheduleExecutionRequestDto request)
        {
            return await _executionQueryService.GetScheduleExecutionAsync(request);
        }
    }
}
