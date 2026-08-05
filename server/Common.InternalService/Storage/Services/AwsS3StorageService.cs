using Amazon.S3;
using Amazon.S3.Model;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class AwsS3StorageService : IStorageService
    {
        protected readonly AmazonS3Client _s3Client;
        protected readonly string _bucketName;

        public AwsS3StorageService(IConfiguration configuration)
        {
            var accessKey = StorageProvider.AccessKey;
            var secretKey = StorageProvider.SecretKey;
            var region = StorageProvider.Region;
            _bucketName = BlocksContext.GetContext()?.TenantId ?? string.Empty;

            _s3Client = new AmazonS3Client(accessKey, secretKey, Amazon.RegionEndpoint.GetBySystemName(region));

            EnsureBucketExistsAsync().GetAwaiter().GetResult();
        }

        protected internal AwsS3StorageService(AmazonS3Client s3Client)
        {
            _s3Client = s3Client;
            _bucketName = BlocksContext.GetContext()?.TenantId.ToLower() ?? string.Empty;

            EnsureBucketExistsAsync().GetAwaiter().GetResult();
        }

        public async Task<Stream?> DownloadFileAsync(string fileName, string? projectKey = null, string? itemId = null, string? versionId = null)
        {
            try
            {
                var response = await _s3Client.GetObjectAsync(_bucketName, fileName);
                return response.ResponseStream;
            }
            catch (AmazonS3Exception)
            {
                return null;
            }
        }

        public async Task<IEnumerable<string>> ListFilesAsync()
        {
            var request = new ListObjectsV2Request { BucketName = _bucketName };
            var response = await _s3Client.ListObjectsV2Async(request);
            return response.S3Objects.Select(o => o.Key).ToList();
        }

        public async Task<bool> DeleteFileAsync(string fileInfo)
        {
            await _s3Client.DeleteObjectAsync(_bucketName, fileInfo);
            return true;
        }

        public string GeneratePreSignedUploadUrlAsync(string fileName, TimeSpan expiry)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                Expires = DateTime.UtcNow.Add(expiry),
                Verb = HttpVerb.PUT
            };
            return _s3Client.GetPreSignedURL(request);
        }

        public async Task<string?> GetDownloadUrlAsync(DownloadUrlRequest request)
        {
            var finalRequest = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = request.FileName,
                Expires = DateTime.UtcNow.Add(request.ExpiryDuration),
                Verb = HttpVerb.GET
            };

            return await _s3Client.GetPreSignedURLAsync(finalRequest);
        }

        public Task<bool> UploadFileToSftpAsync(string fileName, string projectKey, string itemId, string versionId, IFormFile file)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Ensures the bucket exists, creating it if necessary.
        /// </summary>
        protected virtual async Task EnsureBucketExistsAsync()
        {
            if (string.IsNullOrWhiteSpace(_bucketName))
                return;

            try
            {
                await _s3Client.GetBucketLocationAsync(_bucketName);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var putBucketRequest = new PutBucketRequest
                {
                    BucketName = _bucketName,
                    UseClientRegion = true
                };
                await _s3Client.PutBucketAsync(putBucketRequest);
            }
        }
    }
}
