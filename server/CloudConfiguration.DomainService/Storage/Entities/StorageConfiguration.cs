using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Storage.Entities
{
    public class StorageConfiguration : BaseEntity
    {
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public string SecretKey { get; set; }
        public string AccessKey { get; set; }
        public string StorageStrategy { get; set; }
        public string CloudStorageRegionEndPoint { get; set; }

        #region LocalStorage

        public string Host { get; set; }
        public string Port { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string RemoteBasePath { get; set; }
        public string SftpSecretKey { get; set; }

        #endregion

        #region Phase1UploadSecurity

        /// <summary>Lifetime of a generated upload URL, in seconds. Null/missing resolves to blocks-data's documented default (600).</summary>
        public int? UploadUrlExpirySeconds { get; set; }

        /// <summary>Lifetime of a generated download URL, in seconds. Null/missing resolves to blocks-data's documented default (300).</summary>
        public int? DownloadUrlExpirySeconds { get; set; }

        /// <summary>Maximum accepted upload size, in bytes. Null/missing resolves to blocks-data's documented default (5 MiB).</summary>
        public long? MaxFileSizeInBytes { get; set; }

        /// <summary>
        /// Access modifiers ("Public"/"Private") for which upload completion (quarantine + synchronous
        /// verification) is required. Stored as plain strings — not blocks-data's <c>AccessModifier</c> enum,
        /// which this project does not reference — because blocks-data reads this same document and
        /// deserializes the field as a string-represented enum list.
        /// </summary>
        public List<string> UploadCompletionRequiredFor { get; set; }

        #endregion
    }
}
