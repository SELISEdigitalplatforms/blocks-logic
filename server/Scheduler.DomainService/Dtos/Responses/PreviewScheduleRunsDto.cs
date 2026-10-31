using Blocks.Genesis;

namespace Scheduler.DomainService.Dtos.Responses
{
    public class PreviewScheduleRunsDto
    {
        /// <summary>Upcoming fires in UTC, earliest first; fewer than requested when the window ends.</summary>
        public List<DateTime> OccurrencesUtc { get; set; } = [];
    }

    /// <summary>Carries <c>IsSuccess</c> itself, since the Genesis query envelopes do not.</summary>
    public class PreviewScheduleRunsResponseDto : BaseQueryResponse<PreviewScheduleRunsDto>
    {
        public bool IsSuccess { get; set; }
    }
}
