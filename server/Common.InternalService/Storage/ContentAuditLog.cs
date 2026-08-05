using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Storage
{
    /// <summary>
    /// One recorded access decision or access-management action against a directory or file.
    /// Denied attempts are recorded as well as granted ones, so the log answers who tried
    /// as well as who succeeded.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class ContentAuditLog : BaseEntity
    {
        public string TenantId { get; set; } = string.Empty;

        public string ResourceId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public ContentResourceType ResourceType { get; set; }

        public string UserId { get; set; } = string.Empty;

        /// <summary>One of View, Download, Edit, Delete, Manage, Grant, Revoke or Share.</summary>
        public string Action { get; set; } = string.Empty;

        public bool Granted { get; set; }

        /// <summary>Free-form context such as a version number or the principal that was granted.</summary>
        public string? Detail { get; set; }
    }
}
