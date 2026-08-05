using Blocks.Genesis;

namespace Common.InternalService.Storage
{
    public class FileVersion : BaseEntity
    {
        public long No { get; private set; }
        public string? FileId { get; private set; }
        public long SizeInBytes { get; set; }
        public string? TenantId { get; private set; }

        /// <summary>
        /// Object key for this version. Null on legacy and migrated rows, where the
        /// caller falls back to the existing path computation.
        /// </summary>
        public string? StorageKey { get; set; }

        /// <summary>Who uploaded this version.</summary>
        public string? UploadedBy { get; set; }

        private FileVersion() { }

        public static FileVersion CreateNew(string fileId, long no, FileVersionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            return new FileVersion
            {
                FileId = fileId,
                No = options.LazyUpdate ? -no : no,
                ItemId = options.ItemId,
                TenantId = options.TenantId,
                CreatedDate = options.CreateDate,
                CreatedBy = options.CreatedBy,
                Tags = options.Tags,
                Language = options.Language,
                StorageKey = options.StorageKey,
                UploadedBy = options.UploadedBy
            };
        }
    }

    public class FileVersionOptions
    {
        public string ItemId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
        public List<string>? Tags { get; set; }
        public string Language { get; set; } = "en";
        public bool LazyUpdate { get; set; } = false;
        public string? StorageKey { get; set; }
        public string? UploadedBy { get; set; }
    }
}
