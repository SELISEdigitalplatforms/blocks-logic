using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.DomainService.Entities
{
    /// <summary>
    /// An append-only snapshot of one proxy configuration change. Persisted to the per-tenant
    /// <c>ProxyVersions</c> collection and never deleted &mdash; rows are retained even after the owning
    /// <see cref="ProxyDetailEntity"/> is hard-deleted, so the console's <em>Change history</em> tab keeps working.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class ProxyVersionEntity : BaseEntity
    {
        /// <summary>Owning tenant id.</summary>
        public required string TenantId { get; set; }

        /// <summary><see cref="BaseEntity.ItemId"/> of the <see cref="ProxyDetailEntity"/> this version belongs to.</summary>
        public required string ProxyId { get; set; }

        /// <summary>Monotonic per <see cref="ProxyId"/>, starting at 1.</summary>
        public int VersionNumber { get; set; }

        /// <summary>What kind of change this row records.</summary>
        [BsonRepresentation(BsonType.String)]
        public ProxyVersionKind Kind { get; set; }

        /// <summary>Human sentence, e.g. "Proxy created", "Configuration updated", "Reverted to v3".</summary>
        public string ChangeSummary { get; set; } = string.Empty;

        /// <summary>
        /// The per-field edits this row records (empty for <see cref="ProxyVersionKind.Create"/> /
        /// <see cref="ProxyVersionKind.Delete"/>). Replaces the old opaque <c>Before</c> / <c>After</c> lines.
        /// </summary>
        public List<ProxyFieldChange> Changes { get; set; } = new();

        /// <summary>The full effective config after this change (for Delete: the config as it was just before deletion).</summary>
        public required ProxyConfigSnapshot Snapshot { get; set; }

        /// <summary>
        /// Display name of the user who made the change, captured from the ambient <c>BlocksContext</c> at write
        /// time. <c>null</c> when the context carried no name (older rows, or system-initiated changes); the
        /// console then falls back to resolving <see cref="BaseEntity.CreatedBy"/> against IAM.
        /// </summary>
        public string? CreatedByName { get; set; }
    }
}
