namespace Common.InternalService.Monitor
{
    public class IncidentListSummary
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? DowntimeDurationSeconds { get; set; }
    }
}
