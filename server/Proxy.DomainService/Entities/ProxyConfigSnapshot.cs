using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// The full effective proxy configuration captured on a <see cref="ProxyVersionEntity"/> immediately
    /// after the change it describes (or, for <see cref="ProxyVersionKind.Delete"/>, as it was just before
    /// deletion). Drives the console's <em>Revert</em> button.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyConfigSnapshot
    {
        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string Upstream { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public List<HttpMethodType> Methods { get; set; } = new();

        public bool Enabled { get; set; }

        public List<ProxyKeyValue> Headers { get; set; } = new();

        public List<ProxyKeyValue> Query { get; set; } = new();

        /// <summary>
        /// Fields merged into the top level of the client's JSON body on forward. Empty ⇒ body forwarded
        /// unchanged. Captured per version so Revert round-trips a <c>body:&lt;key&gt;</c> change.
        /// </summary>
        public List<ProxyKeyValue> BodyMerge { get; set; } = new();

        /// <summary>Per-method overrides captured with the rest of the config. Empty until Phase D-feature.</summary>
        public List<ProxyMethodConfig> MethodConfigs { get; set; } = new();

        /// <summary>
        /// Response-body treatment on forward, captured per version so Revert round-trips a
        /// <c>responseMode</c> change. <see cref="ProxyResponseMode.All"/> ⇒ relayed unchanged.
        /// </summary>
        [BsonRepresentation(BsonType.String)]
        public ProxyResponseMode ResponseMode { get; set; } = ProxyResponseMode.All;

        /// <summary>
        /// Field paths kept under <see cref="ProxyResponseMode.Select"/>, captured per version so Revert
        /// round-trips a <c>response:&lt;path&gt;</c> change.
        /// </summary>
        public List<string> ResponseInclude { get; set; } = new();
    }
}
