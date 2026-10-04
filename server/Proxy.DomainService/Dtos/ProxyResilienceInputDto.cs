namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Timeout, retry and breaker settings as the API receives them. Every member is optional and
    /// nothing is substituted when one is absent — an omitted member means "not configured", never
    /// "use the usual value".
    /// </summary>
    public sealed class ProxyResilienceInputDto
    {
        public int? TimeoutSeconds { get; set; }

        public ProxyRetryInputDto? Retry { get; set; }

        public ProxyBreakerInputDto? Breaker { get; set; }
    }

    public sealed class ProxyRetryInputDto
    {
        public int? Attempts { get; set; }

        /// <summary>"None", "Fixed" or "Exponential".</summary>
        public string? Backoff { get; set; }

        public int? InitialDelaySeconds { get; set; }

        /// <summary>
        /// Must be true before <see cref="Attempts"/> may exceed 1. The platform cannot tell whether an
        /// upstream tolerates a repeated request, so the tenant says so.
        /// </summary>
        public bool? Idempotent { get; set; }
    }

    public sealed class ProxyBreakerInputDto
    {
        public int? FailureThreshold { get; set; }

        public int? OpenSeconds { get; set; }
    }
}
