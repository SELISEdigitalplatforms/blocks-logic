using System.Net.Http.Headers;
using Blocks.Genesis;
using StorageStrategyCategory = Common.InternalService.Storage.StorageStrategyCategory;
using StorageTypes = Common.InternalService.Storage.StorageTypes;
using DomainService.Configuration;
using DomainService.Storage;
using Blocks.Genesis.Entities.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageDriver;

namespace Workflow.DomainService.Services
{
    /// <summary>Identity and service-principal settings for webhook uploads, bound from <c>WebhookUploads</c>.</summary>
    public sealed class WebhookUploadOptions
    {
        public const string SectionName = "WebhookUploads";

        /// <summary>
        /// Principal that stores files for webhooks with no authenticated caller (<c>authType</c> none). Storage is
        /// default-deny, so this principal needs an Edit grant on each folder such webhooks write to.
        /// </summary>
        public string SystemUserId { get; set; } = "blocks-logic-webhook";

        /// <summary>Time allowed for one upload to the storage provider.</summary>
        public int UploadTimeoutSeconds { get; set; } = 120;
    }

    /// <summary>Where and as whom a webhook's files are stored.</summary>
    public sealed class WebhookFileTarget
    {
        public required string TenantId { get; init; }

        /// <summary>The authenticated caller; null for an anonymous webhook (the service principal is used).</summary>
        public BlocksContext? Caller { get; init; }
        public string? FolderId { get; init; }
        public string? ConfigurationName { get; init; }
    }

    public interface IWebhookFileStore
    {
        /// <summary>Stores one file and returns its file id, or throws <see cref="WebhookIntakeException"/>.</summary>
        Task<string> StoreAsync(WebhookPendingFile file, WebhookFileTarget target, CancellationToken ct = default);

        /// <summary>Best-effort permanent delete of a file stored earlier in the same request.</summary>
        Task<bool> DeleteAsync(string fileId, WebhookFileTarget target, CancellationToken ct = default);
    }

    /// <summary>
    /// Stores webhook files through the in-process storage driver, as the Storage controller and Mail attachments
    /// already do: a pre-signed upload (plus completion when the provider quarantines) for cloud configurations,
    /// or the driver's local upload for SFTP-backed ones. Runs under the caller's <see cref="BlocksContext"/> or
    /// the configured service principal, and restores the ambient context afterwards.
    /// </summary>
    public sealed class StorageWebhookFileStore : IWebhookFileStore
    {
        public const string HttpClientName = "WebhookUploads";
        private const string DefaultConfigurationName = "Default";
        private const string PrivateAccess = "Private";

        private readonly IStorageDriverService? _driver;
        private readonly IStorageConfigurationProvider? _configurations;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly WebhookUploadOptions _options;
        private readonly ILogger<StorageWebhookFileStore> _logger;

        public StorageWebhookFileStore(
            IHttpClientFactory httpClientFactory,
            IOptions<WebhookUploadOptions> options,
            ILogger<StorageWebhookFileStore> logger,
            IStorageDriverService? driver = null,
            IStorageConfigurationProvider? configurations = null)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
            _driver = driver;
            _configurations = configurations;
        }

        public async Task<string> StoreAsync(WebhookPendingFile file, WebhookFileTarget target, CancellationToken ct = default)
        {
            if (_driver is null)
            {
                throw Unavailable(null);
            }

            var restore = BlocksContext.GetContext();
            BlocksContext.SetContext(ActingContext(target, restore), false);
            try
            {
                return await IsLocalAsync(target.ConfigurationName, ct)
                    ? await StoreLocalAsync(file, target)
                    : await StoreCloudAsync(file, target, ct);
            }
            catch (WebhookIntakeException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Unavailable(ex);
            }
            finally
            {
                BlocksContext.SetContext(restore, false);
            }
        }

        public async Task<bool> DeleteAsync(string fileId, WebhookFileTarget target, CancellationToken ct = default)
        {
            if (_driver is null) return false;

            var restore = BlocksContext.GetContext();
            BlocksContext.SetContext(ActingContext(target, restore), false);
            try
            {
                var response = await _driver.DeleteFileAsync(new DeleteFileRequest
                {
                    FileId = fileId,
                    ConfigurationName = target.ConfigurationName!,
                    Permanent = true,
                });
                return response?.IsSuccess == true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Webhook upload rollback: a stored file could not be deleted ({ErrorType}).", ex.GetType().Name); // NOSONAR S6667 — type only
                return false;
            }
            finally
            {
                BlocksContext.SetContext(restore, false);
            }
        }

        private async Task<string> StoreCloudAsync(WebhookPendingFile file, WebhookFileTarget target, CancellationToken ct)
        {
            var session = await _driver!.GetPerSignedUrlForUploadAsync(new GetPreSignedUrlForUploadRequest
            {
                Name = file.Name,
                ParentDirectoryId = target.FolderId,
                ConfigurationName = target.ConfigurationName,
                AccessModifier = PrivateAccess,
                ObjectAccessLevel = ObjectAccessLevel(target),
                SizeInBytes = file.Length,
                ContentType = file.ContentType,
            });
            if (session is null || !session.IsSuccess || string.IsNullOrWhiteSpace(session.UploadUrl))
            {
                throw RefusedBy(session?.Errors, file.Name);
            }

            using (var client = _httpClientFactory.CreateClient(HttpClientName))
            {
                client.Timeout = TimeSpan.FromSeconds(_options.UploadTimeoutSeconds);
                await using var content = file.File.OpenReadStream();
                using var put = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl)
                {
                    Content = new StreamContent(content),
                };
                put.Content.Headers.ContentLength = file.Length;
                put.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type)
                    ? type
                    : new MediaTypeHeaderValue("application/octet-stream");
                foreach (var (name, value) in session.RequiredHeaders ?? new Dictionary<string, string>())
                {
                    if (!put.Headers.TryAddWithoutValidation(name, value))
                    {
                        put.Content.Headers.Remove(name);
                        put.Content.Headers.TryAddWithoutValidation(name, value);
                    }
                }

                using var response = await client.SendAsync(put, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Webhook upload: the storage provider answered {Status}.", (int)response.StatusCode);
                    throw Unavailable(null);
                }
            }

            if (session.UploadCompletionRequired)
            {
                var completion = await _driver.CompleteUploadAsync(new CompleteUploadRequest
                {
                    FileId = session.FileId,
                    FileVersionId = session.FileVersionId ?? string.Empty,
                });
                if (completion is null)
                {
                    throw Unavailable(null);
                }
                if (completion.VerificationStatus != FileVerificationStatus.Verified)
                {
                    throw IsSizeError(completion.RejectionReason)
                        ? StorageLimit(file.Name)
                        : new WebhookIntakeException(422, "storage_rejected", $"File '{file.Name}' was rejected by storage");
                }
            }

            return session.FileId;
        }

        private async Task<string> StoreLocalAsync(WebhookPendingFile file, WebhookFileTarget target)
        {
            var response = await _driver!.UploadFileToLocalStorageAsync(new LocalStorageUploadRequest
            {
                Name = file.Name,
                ParentDirectoryId = target.FolderId!,
                ConfigurationName = target.ConfigurationName,
                AccessModifier = PrivateAccess,
                ObjectAccessLevel = ObjectAccessLevel(target),
                File = file.File,
                MetaData = string.Empty,
                Tags = string.Empty,
            });
            if (response is null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.FileId))
            {
                throw RefusedBy(response?.Errors, file.Name);
            }
            return response.FileId;
        }

        private async Task<bool> IsLocalAsync(string? configurationName, CancellationToken ct)
        {
            if (_configurations is null) return false;
            var configuration = await _configurations.GetByNameAsync(configurationName ?? DefaultConfigurationName, ct);
            return configuration is not null
                && StorageTypes.TryGetCategory(configuration.StorageStrategy ?? string.Empty, out var category)
                && category == StorageStrategyCategory.Local;
        }

        /// <summary>The caller as validated by the webhook's auth; otherwise the service principal in the webhook's tenant.</summary>
        private BlocksContext ActingContext(WebhookFileTarget target, BlocksContext? current)
        {
            if (target.Caller is not null && !string.IsNullOrWhiteSpace(target.Caller.UserId))
            {
                return target.Caller;
            }

            return BlocksContext.Create(
                tenantId: target.TenantId,
                roles: [],
                userId: _options.SystemUserId,
                isAuthenticated: false,
                requestUri: string.Empty,
                organizationId: current?.OrganizationId ?? string.Empty,
                expireOn: DateTime.MinValue,
                email: string.Empty,
                permissions: [],
                userName: _options.SystemUserId,
                phoneNumber: string.Empty,
                displayName: _options.SystemUserId,
                oauthToken: string.Empty,
                originalTenantId: target.TenantId,
                applicationDomain: string.Empty);
        }

        private static string ObjectAccessLevel(WebhookFileTarget target) =>
            target.Caller is not null && !string.IsNullOrWhiteSpace(target.Caller.UserId) ? "Creator" : "Organization";

        private static WebhookIntakeException RefusedBy(IDictionary<string, string>? errors, string fileName)
        {
            var values = errors?.Values ?? (ICollection<string>)Array.Empty<string>();
            if (values.Any(IsSizeError)) return StorageLimit(fileName);
            if (errors?.TryGetValue("access", out var access) == true && access == "forbidden")
            {
                return new WebhookIntakeException(403, "storage_forbidden", "Not allowed to store files in the configured folder");
            }
            return new WebhookIntakeException(422, "storage_rejected", $"File '{fileName}' was rejected by storage");
        }

        private static bool IsSizeError(string? reason) =>
            reason is "declared_size_exceeds_maximum_allowed" or "actual_size_exceeds_maximum_allowed";

        private static WebhookIntakeException StorageLimit(string fileName) =>
            new(413, "storage_too_large", $"File '{fileName}' exceeds the storage limit");

        private static WebhookIntakeException Unavailable(Exception? inner) =>
            new(503, "storage_unavailable", "File storage is unavailable", inner);
    }
}
