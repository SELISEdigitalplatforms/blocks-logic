namespace Scheduler.DomainService.Dtos.Requests
{
    public class GetSchedulesRequestDto
    {
        public string SearchKey { get; set; }
        public int PageNumber { get; set; } = 0;
        public int PageSize { get; set; } = 10;
    }
}
