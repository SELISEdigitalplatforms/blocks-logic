namespace Common.InternalService.Monitor
{
    public class SaveMonitorConfigurationRequest
    {
        public string? ProjectKey { get; set; }
        public string? RepoId { get; set; }
        public string? RepoName { get; set; }
        public string? ExternalServiceId { get; set; }
        public string? ExternalServiceName { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }

        public string? MonitorType { get; set; }
        public string? ProtocolType { get; set; }
        public string? HttpMethodType { get; set; }
        public string? AuthorizationType { get; set; }

        public int? IntervalInSeconds { get; set; }
        public int? TimeoutInSeconds { get; set; }
        public bool IsActive { get; set; }

        public string? MonitorSourceType { get; set; }

        public string? ExpectedContent { get; set; }
        public string? CustomHttpHeaders { get; set; }
        public string? CustomPayload { get; set; }

        public List<string> SuccessHttpResponseCodes { get; set; } = new List<string>();
        public List<string> Regions { get; set; } = new List<string>();
        public List<string> Emails { get; set; } = new List<string>();
    }

    public class UpdateMonitorConfigurationRequest : SaveMonitorConfigurationRequest
    {
        public string? ItemId { get; set; }
    }
}
