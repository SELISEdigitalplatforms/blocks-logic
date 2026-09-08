using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// One audited attempt to forward a client call through a configured proxy: what came in, the URL Blocks
    /// actually called, the outcome, and the full upstream response body. Persisted to the per-tenant
    /// <c>ProxyExecutions</c> collection, one row per data-plane attempt (including pre-flight rejections and
    /// failures). The console's <em>Test</em> action runs the same pipeline but never inserts a row.
    /// <para>
    /// Phase 3 reads these rows to build the <em>Request logs</em> tab, the overview metrics, and the CSV
    /// export; nothing in this phase reads them back.
    /// </para>
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyExecutionEntity : BaseEntity
    {
        /// <summary>Owning tenant id.</summary>
        public required string TenantId { get; set; }

        /// <summary><see cref="BaseEntity.ItemId"/> of the <see cref="ProxyDetailEntity"/> that was called.</summary>
        public required string ProxyId { get; set; }

        /// <summary>Snapshot of the proxy slug at call time.</summary>
        public required string ProxySlug { get; set; }

        /// <summary>
        /// Always <c>false</c> for data-plane rows. Kept for forward-compat; the <c>Test</c> endpoint never
        /// inserts a row in this phase.
        /// </summary>
        public bool IsTest { get; set; }

        /// <summary>Upper-case HTTP method received from the client.</summary>
        public required string RequestMethod { get; set; }

        /// <summary>The client-facing path, e.g. <c>/api/proxy/gateway/stripe-payments/ch_123</c>.</summary>
        public required string RequestPath { get; set; }

        /// <summary>
        /// The raw incoming query string (<c>""</c> when none), WITHOUT any configured params (those are
        /// recorded in <see cref="InjectedQueryKeys"/>).
        /// </summary>
        public required string RequestQuery { get; set; }

        /// <summary>
        /// The fully-rebuilt absolute URL actually called, with the merged query string. Configured secret
        /// values are never stored here: secret-ref entries keep their <c>${SECRET.NAME}</c> token.
        /// </summary>
        public required string UpstreamUrl { get; set; }

        /// <summary>Host of <see cref="UpstreamUrl"/> (for the logs list and metrics grouping).</summary>
        public required string UpstreamHost { get; set; }

        /// <summary>Header names attached by Blocks (values are never stored).</summary>
        public List<string> InjectedHeaderKeys { get; set; } = new();

        /// <summary>Query-parameter names appended by Blocks.</summary>
        public List<string> InjectedQueryKeys { get; set; } = new();

        /// <summary>
        /// HTTP status returned TO THE CLIENT (SPEC &sect;3.4 mapping). <c>0</c> only if the pipeline threw
        /// before a status was decided (guarded; should not happen).
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The third party's status, or <c>null</c> when no response was received (DNS/connect failure,
        /// timeout, non-HTTP error).
        /// </summary>
        public int? UpstreamStatusCode { get; set; }

        /// <summary>One of <see cref="ProxyExecutionOutcome"/>.</summary>
        public required string Outcome { get; set; }

        /// <summary>
        /// Wall-clock milliseconds from just-before-send to response-fully-read (or to the failure).
        /// <c>0</c> for pre-flight rejections (RequestTooLarge / MethodNotAllowed / ProxyNotFound /
        /// Unauthorized).
        /// </summary>
        public int LatencyMs { get; set; }

        /// <summary>Size of the upstream response body in bytes (<c>0</c> when none).</summary>
        public long ResponseBodyBytes { get; set; }

        /// <summary>
        /// The full upstream response body decoded as UTF-8 text, written through
        /// <see cref="Utils.ExecutionBodyStore.Capture"/> so a later truncation policy is a one-method change.
        /// <c>null</c> when there was no response body.
        /// </summary>
        public string? ResponseBody { get; set; }

        /// <summary>Upstream response <c>Content-Type</c> header value, or <c>null</c>.</summary>
        public string? ResponseContentType { get; set; }

        /// <summary>
        /// Short, safe diagnostic for Timeout / UpstreamUnreachable / InternalError; <c>null</c> on success.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>When the attempt started (UTC). Also copied to <see cref="BaseEntity.CreatedDate"/>.</summary>
        public DateTime StartedAtUtc { get; set; }

        /// <summary>When the attempt finished (UTC).</summary>
        public DateTime FinishedAtUtc { get; set; }
    }
}
