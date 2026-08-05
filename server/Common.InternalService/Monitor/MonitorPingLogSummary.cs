using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Monitor
{
    [BsonIgnoreExtraElements]
    public class MonitorPingLogSummary
    {
        public string MonitorId { get; set; }
        public DateTime Timestamp { get; set; }
        public double ResponseTimeMs { get; set; }
        public int StatusCode { get; set; }
    }
}
