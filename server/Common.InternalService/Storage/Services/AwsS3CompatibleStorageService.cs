using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class AwsS3CompatibleStorageService : AwsS3StorageService
    {
        public AwsS3CompatibleStorageService(IConfiguration configuration)
            : base(CreateS3Client())
        {
        }

        private static AmazonS3Client CreateS3Client()
        {
            // Force Signature Version 4 for MinIO/S3-compatible storage
            AWSConfigsS3.UseSignatureVersion4 = true;

            var s3Config = new AmazonS3Config
            {
                ServiceURL = StorageProvider.Host,
                ForcePathStyle = true
            };
            var accessKey = StorageProvider.AccessKey;
            var secretKey = StorageProvider.SecretKey;

            return new AmazonS3Client(accessKey, secretKey, s3Config);
        }
    }
}
