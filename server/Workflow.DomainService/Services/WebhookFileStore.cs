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
    /// <summary>Settings for webhook uploads, bound from <c>WebhookUploads</c>.</summary>
    public sealed class WebhookUploadOptions
    {
        public const string SectionName = "WebhookUploads";

        /// <summary>Time allowed for one upload to the storage provider.</summary>
        public int UploadTimeoutSeconds { get; set; } = 120;
    }

    /// <summary>
    /// As whom a webhook's files are stored. Where is fixed: Storage's default folder in the default configuration
    /// (SPEC10 D2), so the driver is always called without a folder or a configuration name.
    /// </summary>
    public sealed class WebhookFileTarget
    {
        public required string TenantId { get; init; }

        /// <summary>The authenticated caller. Required for storing: there is no service principal (SPEC10 K6).</summary>
        public BlocksContext? Caller { get; init; }
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
    /// or the driver's local upload for SFTP-backed ones. Runs under the caller's <see cref="BlocksContext"/> and
    /// restores the ambient context afterwards.
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
            var caller = CallerOf(target);
            if (_driver is null)
            {
                throw Unavailable(null);
            }

            var restore = BlocksContext.GetContext();
            BlocksContext.SetContext(caller, false);
            try
            {
                return await IsLocalAsync(ct)
                    ? await StoreLocalAsync(file)
                    : await StoreCloudAsync(file, ct);
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
            if (_driver is null || string.IsNullOrWhiteSpace(target.Caller?.UserId)) return false;

            var restore = BlocksContext.GetContext();
            BlocksContext.SetContext(target.Caller, false);
            try
            {
                var response = await _driver.DeleteFileAsync(new DeleteFileRequest
                {
                    FileId = fileId,
                    ConfigurationName = null!,
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

        private async Task<string> StoreCloudAsync(WebhookPendingFile file, CancellationToken ct)
        {
            var session = await _driver!.GetPerSignedUrlForUploadAsync(new GetPreSignedUrlForUploadRequest
            {
                Name = file.Name,
                ParentDirectoryId = null,
                ConfigurationName = null,
                AccessModifier = PrivateAccess,
                ObjectAccessLevel = OrganizationAccess,
                SizeInBytes = file.Length,
                ContentType = file.ContentType,
            });
            if (session is null || !session.IsSuccess || string.IsNullOrWhiteSpace(session.UploadUrl))
            {
                throw RefusedBy(session?.Errors, file.Name, session?.UploadUrl);
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

        private async Task<string> StoreLocalAsync(WebhookPendingFile file)
        {
            var response = await _driver!.UploadFileToLocalStorageAsync(new LocalStorageUploadRequest
            {
                Name = file.Name,
                ParentDirectoryId = null!,
                ConfigurationName = DefaultConfigurationName,
                AccessModifier = PrivateAccess,
                ObjectAccessLevel = OrganizationAccess,
                File = file.File,
                MetaData = string.Empty,
                Tags = string.Empty,
            });
            if (response is null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.FileId))
            {
                throw RefusedBy(response?.Errors, file.Name, null);
            }
            return response.FileId;
        }

        private async Task<bool> IsLocalAsync(CancellationToken ct)
        {
            if (_configurations is null) return false;
            var configuration = await _configurations.GetByNameAsync(DefaultConfigurationName, ct);
            return configuration is not null
                && StorageTypes.TryGetCategory(configuration.StorageStrategy ?? string.Empty, out var category)
                && category == StorageStrategyCategory.Local;
        }

        /// <summary>Organization-visible, so Client Credential runs and same-organization users can reach webhook files (SPEC11 D3).</summary>
        private const string OrganizationAccess = "Organization";

        /// <summary>The caller as validated by the webhook's auth. A missing one is a programming error (SPEC10 K6).</summary>
        private static BlocksContext CallerOf(WebhookFileTarget target) =>
            target.Caller is not null && !string.IsNullOrWhiteSpace(target.Caller.UserId)
                ? target.Caller
                : throw new InvalidOperationException("Webhook files are stored only for an authenticated caller.");

        private static WebhookIntakeException RefusedBy(IDictionary<string, string>? errors, string fileName, string? reason)
        {
            var values = errors?.Values ?? (ICollection<string>)Array.Empty<string>();
            if (values.Any(IsSizeError)) return StorageLimit(fileName);
            if (errors?.TryGetValue("ParentDirectoryId", out var parent) == true && parent == "default_directory_not_found")
            {
                return new WebhookIntakeException(503, "storage_unavailable", "File storage is not set up for this project");
            }
            if (errors?.TryGetValue("access", out var access) == true && access == "forbidden")
            {
                return new WebhookIntakeException(403, "storage_forbidden", "Not allowed to store files in the configured folder");
            }
            var detail = values.Count == 0 ? ReadableReason(reason) : null;
            return new WebhookIntakeException(422, "storage_rejected", detail is null
                ? $"File '{fileName}' was rejected by storage"
                : $"File '{fileName}' was rejected by storage: {detail}");
        }

        private const int MaxReasonLength = 200;

        private static string? ReadableReason(string? reason)
        {
            var text = reason?.Trim();
            if (string.IsNullOrEmpty(text) || Uri.IsWellFormedUriString(text, UriKind.Absolute)) return null;
            return text.Length > MaxReasonLength ? text[..MaxReasonLength] : text;
        }

        private static bool IsSizeError(string? reason) =>
            reason is "declared_size_exceeds_maximum_allowed" or "actual_size_exceeds_maximum_allowed";

        private static WebhookIntakeException StorageLimit(string fileName) =>
            new(413, "storage_too_large", $"File '{fileName}' exceeds the storage limit");

        private static WebhookIntakeException Unavailable(Exception? inner) =>
            new(503, "storage_unavailable", "File storage is unavailable", inner);
    }
}
