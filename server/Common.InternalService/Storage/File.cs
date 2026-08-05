using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Storage
{
    [BsonIgnoreExtraElements]
    public class File : BaseEntity
    {
        public string Url { get; set; }
        public string TenantId { get; set; }

        [BsonRepresentation(BsonType.String)]
        public AccessModifier AccessModifier { get; set; }
        public Dictionary<string, MetaValue>? MetaData { get; set; } = new Dictionary<string, MetaValue>();
        public string Name { get; set; }
        public string? DirectoryId { get; set; }
        public string SystemName { get; set; }
        public StructureType Type { get; set; }
        public string TypeString { get; set; }
        public long CurrentVersion { get; set; }
        public Dictionary<string, string> AdditionalProperties { get; set; } = new Dictionary<string, string>();

        /// <summary>Cached directory ancestry, ordered root first, ending at the parent directory.</summary>
        public List<string> AncestorIds { get; set; } = new();

        /// <summary>When true the effective access policy is resolved by walking <see cref="AncestorIds"/>.</summary>
        public bool InheritsParentAccess { get; set; } = true;
        public string? Extension { get; set; }
        public long SizeInBytes { get; set; }
        public string? ContentType { get; set; }

        /// <summary>Soft delete. Archived files stay queryable so they can be listed in trash and restored.</summary>
        public bool IsArchived { get; set; }
        public bool IsActive { get; set; } = true;
        public string? ConfigurationName { get; set; }
    }
}
