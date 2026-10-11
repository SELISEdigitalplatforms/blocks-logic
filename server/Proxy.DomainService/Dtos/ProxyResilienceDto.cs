namespace Proxy.DomainService.Dtos
{
    /// <summary>Timeout, retry and breaker settings as the API returns them. Absent members stay absent.</summary>
    public sealed class ProxyResilienceDto
    {
        public int? TimeoutSeconds { get; set; }

        public ProxyRetryDto? Retry { get; set; }

        public ProxyBreakerDto? Breaker { get; set; }
    }

    public sealed class ProxyRetryDto
    {
        public int Attempts { get; set; }

        public string Backoff { get; set; } = string.Empty;

        public int InitialDelaySeconds { get; set; }

        public bool Idempotent { get; set; }
    }

    public sealed class ProxyBreakerDto
    {
        public int FailureThreshold { get; set; }

        public int OpenSeconds { get; set; }
    }
}
