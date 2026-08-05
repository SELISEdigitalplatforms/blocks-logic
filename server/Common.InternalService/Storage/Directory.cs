using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Storage
{
    [BsonIgnoreExtraElements]
    public class Directory : Structure
    {
        public string TenantId { get; set; }

        /// <summary>Cached directory ancestry, ordered root first, ending at the parent directory.</summary>
        public List<string> AncestorIds { get; set; } = new();

        /// <summary>Display and search path built from the ancestry, for example "/root/sub/parent".</summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>When true the effective access policy is resolved by walking <see cref="AncestorIds"/>.</summary>
        public bool InheritsParentAccess { get; set; } = true;

        /// <summary>Soft delete. Archived directorys stay queryable so they can be listed in trash and restored.</summary>
        public bool IsArchived { get; set; }
        public bool IsActive { get; set; } = true;
        public string? ConfigurationName { get; set; }
        public string? ModuleName { get; set; }
        public string? Description { get; set; }

        /// <summary>Cached counts and subtree size, maintained on write so listing does not aggregate.</summary>
        public int ChildDirectoryCount { get; set; }
        public int ChildFileCount { get; set; }
        public long SizeInBytes { get; set; }

        public static Directory CreateNew(DirectoryOptions directoryOptions)
        {
            return new Directory
            {
                Name = directoryOptions.Name,
                ParentId = string.IsNullOrEmpty(directoryOptions.ParentId) ? null : directoryOptions.ParentId,
                SystemName = directoryOptions.Name.ToLower(),
                Type = StructureType.Directory,
                TypeString = StructureType.Directory.ToString(),
                MetaData = directoryOptions.MetaData,
                ItemId = directoryOptions.ItemId,
                TenantId = directoryOptions.TenantId,
                CreatedDate = directoryOptions.CreateDate,
                CreatedBy = directoryOptions.CreatedBy,
                LastUpdatedDate = directoryOptions.CreateDate,
                LastUpdatedBy = directoryOptions.CreatedBy,
                Tags = directoryOptions.Tags,
                Language = directoryOptions.Language,
                AllowedFileExtensions = directoryOptions.AllowedFileExtensions,
                AncestorIds = directoryOptions.AncestorIds ?? new(),
                FullPath = directoryOptions.FullPath ?? string.Empty,
                InheritsParentAccess = directoryOptions.InheritsParentAccess,
                ConfigurationName = directoryOptions.ConfigurationName,
                ModuleName = directoryOptions.ModuleName,
                Description = directoryOptions.Description,
            };
        }

        public static Directory CreateNew(string itemId)
        {
            return new Directory { ItemId = itemId };
        }
    }

    public class DirectoryOptions
    {
        public string Name { get; set; }
        public string ParentId { get; set; }
        public Dictionary<string, MetaValue> MetaData { get; set; }
        public string ItemId { get; set; }
        public string TenantId { get; set; }
        public DateTime CreateDate { get; set; }
        public string CreatedBy { get; set; }
        public List<string> Tags { get; set; }
        public string Language { get; set; }
        public string[] AllowedFileExtensions { get; set; }
        public List<string>? AncestorIds { get; set; }
        public string? FullPath { get; set; }
        public bool InheritsParentAccess { get; set; } = true;
        public string? ConfigurationName { get; set; }
        public string? ModuleName { get; set; }
        public string? Description { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class Structure : BaseEntity
    {
        public Structure()
        {
            MetaData = new Dictionary<string, MetaValue>();
        }

        public Dictionary<string, MetaValue> MetaData { get; set; }
        public string Name { get; set; }
        public string? ParentId { get; set; }
        public string SystemName { get; set; }
        public StructureType Type { get; set; }
        public string TypeString { get; set; }
        public string[] AllowedFileExtensions { get; set; }
    }
}
