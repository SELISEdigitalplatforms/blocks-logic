namespace Common.InternalService.Monitor
{
    public class MonitorDateRangeSummaryResponse
    {
        public string Range { get; set; }
        public long TotalDurationMs { get; set; }
        public long IncidentCount { get; set; }
    }
}
