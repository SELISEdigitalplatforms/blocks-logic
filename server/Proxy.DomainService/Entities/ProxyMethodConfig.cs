using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// A per-method override of the shared proxy configuration. Reserved by the schema now; not yet writable
    /// through the API (the validator rejects a non-empty <see cref="ProxyDetailEntity.MethodConfigs"/>).
    /// When a proxy carries a <see cref="ProxyMethodConfig"/> for the method being forwarded, the gateway
    /// resolves <c>override ?? shared</c> for that method's headers / query / upstream; a <c>null</c> member
    /// means "inherit the shared value". An empty <see cref="ProxyDetailEntity.MethodConfigs"/> list is
    /// byte-for-byte today's behaviour.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyMethodConfig
    {
        /// <summary>The method this override applies to. Persisted to Mongo as its string name.</summary>
        [BsonRepresentation(BsonType.String)]
        public HttpMethodType Method { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.Headers"/>.</summary>
        public List<ProxyKeyValue>? Headers { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.Query"/>.</summary>
        public List<ProxyKeyValue>? Query { get; set; }

        /// <summary><c>null</c> ⇒ inherit the shared <see cref="ProxyDetailEntity.Upstream"/>.</summary>
        public string? Upstream { get; set; }
    }
}
