
using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Storage.RequestModel
{
    public class SaveStorageConfigurationRequest 
    {
        public string Name { get; set; }
        public string? ConnectionString { get; set; }
        public string? SecretKey { get; set; }
        public string? AccessKey { get; set; }
        public string StorageStrategy { get; set; }
        public string? CloudStorageRegionEndPoint { get; set; }
 
        public bool UpdateRequest { get; set; }
        public string? ItemId { get; set; }

        #region LocalStorage

        public string? Host { get; set; }
        public string? Port { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? RemoteBasePath { get; set; }

        #endregion

        #region Phase1UploadSecurity

        /// <summary>Lifetime of a generated upload URL, in seconds. Omit/null to keep blocks-data's default (600).</summary>
        public int? UploadUrlExpirySeconds { get; set; }

        /// <summary>Lifetime of a generated download URL, in seconds. Omit/null to keep blocks-data's default (300).</summary>
        public int? DownloadUrlExpirySeconds { get; set; }

        /// <summary>Maximum accepted upload size, in bytes. Omit/null to keep blocks-data's default (5 MiB).</summary>
        public long? MaxFileSizeInBytes { get; set; }

        /// <summary>Access modifiers ("Public"/"Private") for which upload completion is required. Omit/empty for none.</summary>
        public List<string>? UploadCompletionRequiredFor { get; set; }

        #endregion
    }
}
