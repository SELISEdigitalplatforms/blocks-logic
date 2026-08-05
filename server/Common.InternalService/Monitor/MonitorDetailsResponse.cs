namespace Common.InternalService.Monitor
{
    public class MonitorDetailsResponse : BaseApiResponse
    {
        public List<MonitorDateRangeSummaryResponse> DateRangeSummary { get; set; } = new List<MonitorDateRangeSummaryResponse>();
        public List<MonitorIncident> MonitorIncidents { get; set; } = new List<MonitorIncident>();
    }
}
