using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Monitor
{
    [BsonIgnoreExtraElements]
    public class MonitorIncident : BaseEntity
    {
        public string MonitorId { get; set; }
        public string MonitorName { get; set; }
        public string MonitorUrl { get; set; }
        public string ProjectKey { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }

        public MonitorConfigurationTypes? MonitorConfigurationType { get; set; }

        public MonitorSourceTypes? MonitorSourceType { get; set; }

        public int LastStatusCode { get; set; }
        public bool IsResolved { get; set; } = false;

        public string FailureReason { get; set; }
        public string ResponseBody { get; set; }
        public double? DowntimeDurationSeconds =>
            IsResolved && EndTime.HasValue
                ? (EndTime.Value - StartTime).TotalSeconds
                : null;
    }
}
