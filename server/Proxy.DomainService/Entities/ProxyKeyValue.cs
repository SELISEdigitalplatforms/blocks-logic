using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// A single header or query-string parameter that Blocks attaches when it forwards a call to the
    /// upstream endpoint. Embedded in <see cref="ProxyDetailEntity"/> and <see cref="ProxyConfigSnapshot"/>.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyKeyValue
    {
        /// <summary>Header / query-parameter name. 1..256 chars, no leading or trailing whitespace.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Stored verbatim, INCLUDING any <c>${SECRET.NAME}</c> token. Phase 1 does not resolve secret
        /// references; it only records that the value contains one via <see cref="IsSecretRef"/>.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Computed on write: <c>true</c> when <see cref="Value"/> contains a <c>${SECRET.NAME}</c> reference.</summary>
        public bool IsSecretRef { get; set; }
    }
}
