using Blocks.Genesis;

namespace Scheduler.DomainService.Dtos.Responses
{
    /// <summary>
    /// A page of history rows plus <c>TotalCount</c>. Carries <c>IsSuccess</c> itself, since the Genesis query
    /// envelopes do not.
    /// </summary>
    public class GetScheduleExecutionsResponseDto : BaseQueryListResponse<List<ScheduleExecutionListItemDto>>
    {
        public bool IsSuccess { get; set; }
    }

    /// <summary>One full history row. Carries <c>IsSuccess</c> itself, since the Genesis query envelopes do not.</summary>
    public class GetScheduleExecutionResponseDto : BaseQueryResponse<ScheduleExecutionDto>
    {
        public bool IsSuccess { get; set; }
    }
}
