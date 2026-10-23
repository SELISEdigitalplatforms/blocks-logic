using Blocks.Genesis;
using Blocks.Genesis.Entities.Data;

namespace CloudConfiguration.DomainService.Storage.ResponseModel
{
    /// <summary>
    /// The JSON shape the Storage API has always returned for a storage configuration. The shared
    /// <see cref="StorageConfiguration"/> keeps <c>UploadCompletionRequiredFor</c> as an enum list (stored as
    /// strings in Mongo), which System.Text.Json would write as numbers. This response keeps the names
    /// ("Public", "Private") so the API contract does not change.
    /// </summary>
    public class StorageConfigurationResponse : BaseEntity
    {
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public string SecretKey { get; set; }
        public string AccessKey { get; set; }
        public string StorageStrategy { get; set; }
        public string CloudStorageRegionEndPoint { get; set; }
        public string Host { get; set; }
        public string Port { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string RemoteBasePath { get; set; }
        public string SftpSecretKey { get; set; }
        public int? UploadUrlExpirySeconds { get; set; }
        public int? DownloadUrlExpirySeconds { get; set; }
        public long? MaxFileSizeInBytes { get; set; }
        public List<string>? UploadCompletionRequiredFor { get; set; }

        public static StorageConfigurationResponse From(StorageConfiguration c) => new()
        {
            ItemId = c.ItemId,
            CreatedDate = c.CreatedDate,
            LastUpdatedDate = c.LastUpdatedDate,
            CreatedBy = c.CreatedBy,
            Language = c.Language,
            LastUpdatedBy = c.LastUpdatedBy,
            OrganizationId = c.OrganizationId,
            Tags = c.Tags,
            Name = c.Name,
            ConnectionString = c.ConnectionString,
            SecretKey = c.SecretKey,
            AccessKey = c.AccessKey,
            StorageStrategy = c.StorageStrategy,
            CloudStorageRegionEndPoint = c.CloudStorageRegionEndPoint,
            Host = c.Host,
            Port = c.Port,
            UserName = c.UserName,
            Password = c.Password,
            RemoteBasePath = c.RemoteBasePath,
            SftpSecretKey = c.SftpSecretKey,
            UploadUrlExpirySeconds = c.UploadUrlExpirySeconds,
            DownloadUrlExpirySeconds = c.DownloadUrlExpirySeconds,
            MaxFileSizeInBytes = c.MaxFileSizeInBytes,
            UploadCompletionRequiredFor = c.UploadCompletionRequiredFor?.Select(a => a.ToString()).ToList(),
        };
    }
}
