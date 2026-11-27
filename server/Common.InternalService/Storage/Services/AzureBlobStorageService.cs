using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Data;
using System.Diagnostics.CodeAnalysis;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class AzureBlobStorageService : IStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _containerClient;

        public AzureBlobStorageService(StorageConfiguration storageConfiguration)
        {
            var blobServiceClient = new BlobServiceClient(storageConfiguration.ConnectionString);
            _blobServiceClient = blobServiceClient;
            _containerClient = blobServiceClient.GetBlobContainerClient(BlocksContext.GetContext()?.TenantId.ToLower());

            _containerClient.CreateIfNotExists(PublicAccessType.Blob);
        }

        /// <summary>For tests: the clients are given, so no account is contacted at construction.</summary>
        protected internal AzureBlobStorageService(BlobServiceClient blobServiceClient, BlobContainerClient containerClient)
        {
            _blobServiceClient = blobServiceClient;
            _containerClient = containerClient;
        }

        public async Task<Stream?> DownloadFileAsync(string fileName, string? tenantId = null, string? itemId = null, string? versionId = null)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);

            if (await blobClient.ExistsAsync())
            {
                var response = await blobClient.DownloadAsync();
                return response.Value.Content;
            }
            return null;
        }

        public async Task<string?> GetDownloadUrlAsync(DownloadUrlRequest request)
        {
            var blobClient = _containerClient.GetBlobClient(request.FileName);

            if (await blobClient.ExistsAsync())
            {
                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _containerClient.Name,
                    BlobName = request.FileName,
                    Resource = "b",
                    ExpiresOn = DateTimeOffset.UtcNow.Add(request.ExpiryDuration)
                };

                sasBuilder.SetPermissions(BlobContainerSasPermissions.Read);

                return GenerateDownloadUriByAccessType(blobClient.GenerateSasUri(sasBuilder).ToString(), request.AccessModifier);
            }
            return null;
        }

        private static string GenerateDownloadUriByAccessType(string sasUri, AccessModifier accessModifier)
        {
            if (accessModifier == AccessModifier.Public)
                return sasUri.Substring(0, sasUri.IndexOf('?'));

            return sasUri;
        }

        public async Task<IEnumerable<string>> ListFilesAsync()
        {
            var files = new List<string>();

            await foreach (var blob in _containerClient.GetBlobsAsync())
            {
                files.Add(blob.Name);
            }
            return files;
        }

        public async Task<bool> DeleteFileAsync(string fileInfo)
        {
            var blobClient = _containerClient.GetBlobClient(fileInfo);
            await blobClient.DeleteIfExistsAsync();
            return true;
        }

        public string GeneratePreSignedUploadUrlAsync(string fileName, TimeSpan expiry)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerClient.Name,
                BlobName = fileName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Write);

            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        public Task<bool> UploadFileToSftpAsync(string fileName, string tenantId, string itemId, string versionId, Microsoft.AspNetCore.Http.IFormFile file)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Public read on Azure is the container's access level: <c>Blob</c> lets anyone read a blob whose URL they know,
        /// and does not let them list the container. The container is created with that level, and an existing one is
        /// raised to it. The URL is the blob's own address, with no SAS, so it never expires.
        /// </summary>
        public async Task<string> UploadPublicObjectAsync(string container, string key, Stream content, string contentType)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(container);
            await EnsurePublicBlobAccessAsync(containerClient);

            var blobClient = containerClient.GetBlobClient(key);
            await blobClient.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });

            return blobClient.Uri.ToString();
        }

        public async Task<Stream?> OpenReadAsync(string container, string key)
        {
            var blobClient = _blobServiceClient.GetBlobContainerClient(container).GetBlobClient(key);

            try
            {
                var response = await blobClient.DownloadStreamingAsync();
                return response.Value.Content;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        private static async Task EnsurePublicBlobAccessAsync(BlobContainerClient containerClient)
        {
            if (await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob) is not null)
                return;

            var policy = (await containerClient.GetAccessPolicyAsync()).Value;
            if (policy.BlobPublicAccess == PublicAccessType.Blob)
                return;

            // Setting the level replaces the stored access policies unless they are passed back.
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob, policy.SignedIdentifiers);
        }
    }
}
