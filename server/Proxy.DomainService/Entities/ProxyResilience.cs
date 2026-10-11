using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>Shape of the delay between retry attempts.</summary>
    public enum ProxyBackoffKind
    {
        /// <summary>Retry immediately.</summary>
        None = 0,

        /// <summary>Wait <see cref="ProxyRetryConfig.InitialDelaySeconds"/> before every attempt.</summary>
        Fixed = 1,

        /// <summary>Double the wait each attempt, from <see cref="ProxyRetryConfig.InitialDelaySeconds"/>.</summary>
        Exponential = 2,
    }

    /// <summary>
    /// How a route retries a failed forward.
    /// <para>
    /// Absent entirely unless the tenant configures it: no proxy retries by default, because a retry the
    /// operator did not ask for is a second charge, a second email or a second order.
    /// </para>
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyRetryConfig
    {
        /// <summary>Total attempts including the first. 1 is "no retry" and is the same as not configuring this.</summary>
        public int Attempts { get; set; } = 1;

        [BsonRepresentation(BsonType.String)]
        public ProxyBackoffKind Backoff { get; set; } = ProxyBackoffKind.None;

        /// <summary>Delay before the second attempt; the base for <see cref="ProxyBackoffKind.Exponential"/>.</summary>
        public int InitialDelaySeconds { get; set; } = 1;

        /// <summary>
        /// The tenant's assertion that sending this request twice is safe.
        /// <para>
        /// Required before <see cref="Attempts"/> may exceed 1, and the validator refuses the config without
        /// it. GET and HEAD are safe by the HTTP spec; PUT and DELETE are idempotent by the spec but only if
        /// the upstream honours it; POST almost never is. The platform cannot know which, so the tenant says
        /// so explicitly rather than inheriting a guess — a retried POST is how a customer gets charged twice.
        /// </para>
        /// </summary>
        public bool Idempotent { get; set; }
    }

    /// <summary>
    /// The circuit breaker for one upstream host.
    /// <para>
    /// Absent unless configured. When a host is down, every call to it otherwise occupies a connection and a
    /// request thread for the whole timeout — so an upstream outage becomes a gateway outage. Opening the
    /// circuit turns that into an immediate, cheap failure.
    /// </para>
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyBreakerConfig
    {
        /// <summary>Consecutive failures before the circuit opens.</summary>
        public int FailureThreshold { get; set; } = 5;

        /// <summary>How long it stays open before one probe is allowed through.</summary>
        public int OpenSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Timeout, retry and breaker settings for a proxy or one of its routes.
    /// <para>
    /// Every member is optional and nothing is filled in for the tenant. A proxy with no
    /// <c>Resilience</c> at all, or one whose members are null, behaves exactly as it did before this
    /// existed: the named client's own timeout, no retries, no breaker. That is deliberate — these change
    /// what reaches a vendor's API, so they are the tenant's decision to make, not a default to inherit.
    /// </para>
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyResilienceConfig
    {
        /// <summary>
        /// Budget for the whole forward, retries included — not per attempt. <c>null</c> ⇒ the named
        /// client's timeout, which is what every route used before this setting existed.
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary><c>null</c> ⇒ no retries.</summary>
        public ProxyRetryConfig? Retry { get; set; }

        /// <summary><c>null</c> ⇒ no breaker.</summary>
        public ProxyBreakerConfig? Breaker { get; set; }

        /// <summary>True when this object asks for nothing, and so is indistinguishable from absent.</summary>
        public bool IsEmpty => TimeoutSeconds is null && Retry is null && Breaker is null;
    }
}
