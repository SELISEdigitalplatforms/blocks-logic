using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// One endpoint a proxy is allowed to reach, and the configuration that applies to it. A proxy's
    /// <see cref="ProxyDetailEntity.Routes"/> is an allowlist: the forward matches the caller's
    /// <c>{**path}</c> against it and refuses anything unlisted with
    /// <see cref="ProxyExecutionOutcome.RouteNotAllowed"/>, so a caller who never sees the upstream
    /// credential also cannot aim it at an endpoint the tenant did not declare.
    /// <para>
    /// <see cref="Path"/> is the client-facing template and <see cref="UpstreamPath"/> the template it
    /// rewrites to, so the vendor's URL shape is never forced on the front end. Both may carry
    /// <c>{name}</c> parameters, which match exactly one segment and are substituted into the upstream
    /// path; every parameter used by <see cref="UpstreamPath"/> must be declared by <see cref="Path"/>.
    /// </para>
    /// Each per-route member is either <c>null</c> (inherit the proxy's shared value) or an override, which
    /// is what lets two POST routes on the same proxy carry different <see cref="BodyMerge"/> payloads.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyRouteConfig
    {
        /// <summary>The method this route accepts. Must be one of <see cref="ProxyDetailEntity.Methods"/>.</summary>
        [BsonRepresentation(BsonType.String)]
        public HttpMethodType Method { get; set; }

        /// <summary>
        /// Client-facing path template appended after <c>/api/proxy/gateway/{slug}</c>, with no leading or
        /// trailing slash. <c>""</c> is the base path itself. e.g. <c>"orders/{id}/refunds"</c>.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Path template appended to the upstream, with no leading or trailing slash. <c>null</c> ⇒ use
        /// <see cref="Path"/> verbatim. e.g. <c>"v1/charges/{id}/refunds"</c>.
        /// </summary>
        public string? UpstreamPath { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.Headers"/>.</summary>
        public List<ProxyKeyValue>? Headers { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.Query"/>.</summary>
        public List<ProxyKeyValue>? Query { get; set; }

        /// <summary>
        /// <c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.BodyMerge"/>. An explicitly empty
        /// list overrides the shared value with "merge nothing", which is how a route opts out of a
        /// proxy-wide body merge that does not belong in its payload.
        /// </summary>
        public List<ProxyKeyValue>? BodyMerge { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.ResponseMode"/>.</summary>
        [BsonRepresentation(BsonType.String)]
        public ProxyResponseMode? ResponseMode { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.ResponseInclude"/>.</summary>
        public List<string>? ResponseInclude { get; set; }
    }
}
