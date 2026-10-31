using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Services;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Read side of the schedule execution history. Shares the <c>/api/Scheduler/{action}</c> route with
    /// <see cref="SchedulerController"/>, so the URLs are the same as when both lived there.
    /// </summary>
    [ApiController]
    [Route("Scheduler/[action]")]
    public class ScheduleExecutionsController : ControllerBase
    {
        private readonly IScheduleExecutionQueryService _executionQueryService;

        /// <summary>Takes the execution-history query service.</summary>
        public ScheduleExecutionsController(IScheduleExecutionQueryService executionQueryService)
        {
            _executionQueryService = executionQueryService;
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
