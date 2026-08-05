using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Monitor
{
    public enum MonitorTypes
    {
        HTTP,
        PING,
        TCP,
        DNS,
        KEYWORD
    }

    public enum ProtocolTypes
    {
        HTTP,
        HTTPS
    }

    public enum HttpMethodTypes
    {
        HEAD,
        GET,
        POST
    }

    public enum AuthorizationTypes
    {
        NONE,
        BASIC,
        BEARER,
        API_KEY
    }

    public enum MonitorConfigurationTypes
    {
        OutboundPing,
        InboundPing
    }

    public enum MonitorSourceTypes
    {
        Infrastructure,
        DeployedServices,
        BlocksServices,
        ExternalServices,
        OtherServices,
    }

    [BsonIgnoreExtraElements]
    public class MonitorConfiguration : BaseEntity
    {
        public string? TenantId { get; set; }
        public string? ExternalServiceId { get; set; }
        public string? ExternalServiceName { get; set; }
        public string? RepoId { get; set; }
        public string? RepoName { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }

        public MonitorConfigurationTypes MonitorConfigurationType { get; set; }
        public MonitorSourceTypes? MonitorSourceType { get; set; }

        [BsonElement("MonitorSourcetypes")]
        [BsonIgnoreIfNull]
        public MonitorSourceTypes? LegacyMonitorSourceType { get; set; }

        [BsonIgnore]
        public MonitorSourceTypes EffectiveMonitorSourceType =>
            MonitorSourceType ?? LegacyMonitorSourceType ?? MonitorSourceTypes.DeployedServices;

        public MonitorTypes? MonitorType { get; set; }
        public ProtocolTypes? ProtocolType { get; set; }
        public HttpMethodTypes? HttpMethodType { get; set; }
        public AuthorizationTypes? AuthorizationType { get; set; }

        public int IntervalInSeconds { get; set; } = 30;
        public int TimeoutInSeconds { get; set; } = 60;
        public int GracePeriodInSeconds { get; set; } = 30;

        public DateTime? LastIncidentAt { get; set; }
        public DateTime? LastCheckedAt { get; set; }

        public bool IsActive { get; set; } = true;
        public bool CurrentStatus { get; set; } = true;

        public string? ExpectedContent { get; set; }
        public string? CustomHttpHeaders { get; set; }
        public string? CustomPayload { get; set; }
        public List<string> SuccessHttpResponseCodes { get; set; } = new List<string>();
        public List<string> Regions { get; set; } = new List<string>();
        public List<string> Emails { get; set; } = new List<string>();
        public List<IncidentListSummary> IncidentSummaries { get; set; } = new List<IncidentListSummary>();
    }
}
