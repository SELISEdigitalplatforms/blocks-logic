using Amazon;
using Amazon.S3;
using Blocks.Genesis.Entities.Data;
using System.Diagnostics.CodeAnalysis;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class AwsS3CompatibleStorageService : AwsS3StorageService
    {
        public AwsS3CompatibleStorageService(StorageConfiguration storageConfiguration)
            : base(CreateS3Client(storageConfiguration))
        {
        }

        private static AmazonS3Client CreateS3Client(StorageConfiguration storageConfiguration)
        {
            // Force Signature Version 4 for MinIO/S3-compatible storage
            AWSConfigsS3.UseSignatureVersion4 = true;

            var s3Config = new AmazonS3Config
            {
                ServiceURL = storageConfiguration.Host,
                ForcePathStyle = true
            };
            var accessKey = storageConfiguration.AccessKey;
            var secretKey = storageConfiguration.SecretKey;

            return new AmazonS3Client(accessKey, secretKey, s3Config);
        }
    }
}
