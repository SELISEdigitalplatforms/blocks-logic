using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// A named reverse proxy defined by a tenant: a friendly name, one upstream HTTPS endpoint, the HTTP
    /// methods it accepts, and the headers / query parameters Blocks attaches when forwarding. Persisted to
    /// the per-tenant <c>Proxies</c> collection. Every configuration mutation is snapshotted as a
    /// <see cref="ProxyVersionEntity"/>.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyDetailEntity : BaseEntity
    {
        /// <summary>Owning tenant id.</summary>
        public required string TenantId { get; set; }

        /// <summary>Friendly name shown in the console. 1..80 chars after trim.</summary>
        public required string Name { get; set; }

        /// <summary>
        /// Immutable, unique-per-tenant identifier derived once from <see cref="Name"/> at create time.
        /// Never recomputed by Update or Revert.
        /// </summary>
        public required string Slug { get; set; }

        /// <summary>Absolute <c>https://</c> URL of the upstream endpoint. Never exposed unmasked in list views.</summary>
        public required string Upstream { get; set; }

        /// <summary>1..5 of GET, POST, PUT, PATCH, DELETE. De-duplicated, first-occurrence order. Stored in Mongo as string names.</summary>
        [BsonRepresentation(BsonType.String)]
        public List<HttpMethodType> Methods { get; set; } = new();

        /// <summary>Whether the proxy currently accepts traffic. Toggled via the Toggle action only.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Headers Blocks attaches when forwarding a call.</summary>
        public List<ProxyKeyValue> Headers { get; set; } = new();

        /// <summary>Query parameters Blocks appends when forwarding a call.</summary>
        public List<ProxyKeyValue> Query { get; set; } = new();

        /// <summary>
        /// Per-method overrides. Empty ⇒ every method uses the shared <see cref="Headers"/> / <see cref="Query"/>
        /// / <see cref="Upstream"/>. Not yet writable via the API (the validator rejects a non-empty value).
        /// </summary>
        public List<ProxyMethodConfig> MethodConfigs { get; set; } = new();

        /// <summary>Monotonic; equals the highest <see cref="ProxyVersionEntity.VersionNumber"/> written for this proxy.</summary>
        public int CurrentVersion { get; set; }
    }
}
