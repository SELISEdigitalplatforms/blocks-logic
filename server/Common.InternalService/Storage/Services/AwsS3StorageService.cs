using Amazon.S3;
using Amazon.S3.Model;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Blocks.Genesis.Entities.Data;
using System.Diagnostics.CodeAnalysis;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class AwsS3StorageService : IStorageService
    {
        protected readonly AmazonS3Client _s3Client;
        protected readonly string _bucketName;

        public AwsS3StorageService(StorageConfiguration storageConfiguration)
        {
            var accessKey = storageConfiguration.AccessKey;
            var secretKey = storageConfiguration.SecretKey;
            var region = storageConfiguration.CloudStorageRegionEndPoint;
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

        public async Task<Stream?> DownloadFileAsync(string fileName, string? tenantId = null, string? itemId = null, string? versionId = null)
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

        public Task<bool> UploadFileToSftpAsync(string fileName, string tenantId, string itemId, string versionId, IFormFile file)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Public read on S3 is a bucket policy on the named bucket: anyone may read an object whose exact key they know
        /// (<c>s3:GetObject</c>), and nothing else (no listing, write or delete). The bucket is created if missing and the
        /// policy is merged into any it has. The URL is the object's own address, with no signature, so it never expires.
        /// </summary>
        public async Task<string> UploadPublicObjectAsync(string container, string key, Stream content, string contentType)
        {
            await EnsureBucketExistsAsync(container);
            await EnsurePublicReadPolicyAsync(container);

            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = container,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false
            });

            return PublicObjectRules.BuildS3ObjectUrl(
                _s3Client.Config.ServiceURL,
                _s3Client.Config.RegionEndpoint?.GetEndpointForService("s3")?.Hostname,
                container,
                key);
        }

        public async Task<Stream?> OpenReadAsync(string container, string key)
        {
            try
            {
                var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = container, Key = key });
                return response.ResponseStream;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private async Task EnsurePublicReadPolicyAsync(string bucket)
        {
            string? existing = null;
            try
            {
                existing = (await _s3Client.GetBucketPolicyAsync(new GetBucketPolicyRequest { BucketName = bucket })).Policy;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No policy yet.
            }

            var updated = PublicObjectRules.AddPublicReadStatement(existing, bucket);
            if (updated is not null)
            {
                await _s3Client.PutBucketPolicyAsync(new PutBucketPolicyRequest { BucketName = bucket, Policy = updated });
            }
        }

        /// <summary>
        /// Ensures the tenant's bucket exists, creating it if necessary.
        /// </summary>
        protected virtual Task EnsureBucketExistsAsync() => EnsureBucketExistsAsync(_bucketName);

        /// <summary>Ensures the named bucket exists, creating it if necessary.</summary>
        protected async Task EnsureBucketExistsAsync(string bucketName)
        {
            if (string.IsNullOrWhiteSpace(bucketName))
                return;

            try
            {
                await _s3Client.GetBucketLocationAsync(bucketName);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var putBucketRequest = new PutBucketRequest
                {
                    BucketName = bucketName,
                    UseClientRegion = true
                };
                await _s3Client.PutBucketAsync(putBucketRequest);
            }
        }
    }
}
