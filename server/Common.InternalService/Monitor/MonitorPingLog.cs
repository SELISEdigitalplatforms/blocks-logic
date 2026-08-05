using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Monitor
{
    [BsonIgnoreExtraElements]
    public class MonitorPingLog
    {
        public string MonitorId { get; set; }
        public string Url { get; set; }
        public int StatusCode { get; set; }
        public string ResponseMessage { get; set; }
        public string ResponseBody { get; set; }
        public bool IsSuccess { get; set; }
        public double ResponseTimeMs { get; set; }
        public HttpMethodTypes? HttpMethodType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
