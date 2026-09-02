using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StorageDriver;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Resolves storage file ids to attachment content: ask the storage driver for a pre-signed
    /// download URL, fetch it, and hand back the bytes.
    /// </summary>
    public sealed class StorageMailAttachmentResolver : IMailAttachmentResolver
    {
        private static readonly IReadOnlyList<MailAttachment> None = [];

        private readonly IStorageDriverService? _storageDriverService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StorageMailAttachmentResolver> _logger;
        private readonly MailAttachmentOptions _options;

        public StorageMailAttachmentResolver(
            IHttpClientFactory httpClientFactory,
            ILogger<StorageMailAttachmentResolver> logger,
            IOptions<MailAttachmentOptions> options,
            IStorageDriverService? storageDriverService = null)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _options = options.Value;
            _storageDriverService = storageDriverService;
        }

        public async Task<IReadOnlyList<MailAttachment>> ResolveAsync(IEnumerable<string>? fileIds, CancellationToken cancellationToken = default)
        {
            // Duplicate ids would attach the same file twice; blanks are already rejected by the
            // validator, but SendEmailConsumer re-sends without validating so guard here too.
            var ids = (fileIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (ids.Count == 0)
            {
                return None;
            }

            if (_storageDriverService is null)
            {
                throw new MailAttachmentException(
                    $"The mail carries {ids.Count} attachment(s) but no storage driver is registered in this host, so they cannot be resolved.");
            }

            if (ids.Count > _options.MaxAttachmentCount)
            {
                throw new MailAttachmentException(
                    $"The mail carries {ids.Count} attachments, above the limit of {_options.MaxAttachmentCount}.");
            }

            var restore = BlocksContext.GetContext();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolved = new List<MailAttachment>(ids.Count);
            long totalBytes = 0;

            // A caller with a real identity already proved they may ask for this mail; resolving as
            // them means no blanket Download grant is needed. Only a request with nobody to
            // authorise as - the worker, or an anonymous send - falls back to the service principal.
            var useCaller = _options.PreferCallerIdentity && !string.IsNullOrWhiteSpace(restore?.UserId);
            var principal = useCaller ? restore!.UserId : _options.SystemUserId;

            _logger.LogInformation(
                "ATTACHMENTS: resolving {Count} file(s) as {Principal} (caller identity {CallerUsed}).",
                ids.Count, principal, useCaller ? "used" : "unavailable, using the service principal");

            try
            {
                if (!useCaller)
                {
                    EnterSystemContext(restore);
                }

                foreach (var id in ids)
                {
                    var attachment = await ResolveOneAsync(id, principal, usedNames, cancellationToken);

                    totalBytes += attachment.Content.LongLength;
                    if (totalBytes > _options.MaxTotalBytes)
                    {
                        throw new MailAttachmentException(
                            $"Attachments total more than the {_options.MaxTotalBytes} byte limit; stopped at '{id}'.");
                    }

                    resolved.Add(attachment);
                }
            }
            finally
            {
                if (!useCaller)
                {
                    BlocksContext.SetContext(restore, false);
                }
            }

            _logger.LogInformation(
                "Resolved {Count} attachment(s), {Bytes} bytes total, as {Principal}.",
                resolved.Count, totalBytes, principal);

            return resolved;
        }

        private async Task<MailAttachment> ResolveOneAsync(string fileId, string principal, HashSet<string> usedNames, CancellationToken cancellationToken)
        {
            var file = await _storageDriverService!.GetUrlForDownloadFileAsync(new global::DomainService.Storage.GetFileRequest { FileId = fileId });

            if (file is null || string.IsNullOrWhiteSpace(file.Url))
            {
                // Storage is default-deny: with no explicit Allow the resolver's principal is
                // refused, which is indistinguishable here from a missing file. Name both.
                throw new MailAttachmentException(
                    $"Attachment '{fileId}' could not be resolved. It is missing, or the principal " +
                    $"'{principal}' has no Download grant on it.");
            }

            byte[] content;
            using (var client = _httpClientFactory.CreateClient(nameof(StorageMailAttachmentResolver)))
            {
                client.Timeout = TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds);

                using var response = await client.GetAsync(new Uri(file.Url), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new MailAttachmentException(
                        $"Downloading attachment '{fileId}' failed with status {(int)response.StatusCode}. The pre-signed URL may have expired.");
                }

                content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            var fileName = UniqueName(SafeFileName(file.Name, fileId), usedNames);

            _logger.LogInformation(
                "ATTACHMENT RESOLVED: fileId={FileId} name={FileName} bytes={Bytes}",
                fileId, fileName, content.LongLength);

            return new MailAttachment
            {
                FileName = fileName,
                ContentType = MimeTypes.GetMimeType(fileName),
                Content = content
            };
        }

        /// <summary>
        /// Runs resolution as the mail service's own principal, keeping the caller's tenant. Mail
        /// is sent server to server, so the request rarely carries a user to authorise against.
        /// </summary>
        private void EnterSystemContext(BlocksContext? current)
        {
            var tenantId = current?.TenantId ?? string.Empty;

            BlocksContext.SetContext(
                BlocksContext.Create(
                    tenantId: tenantId,
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
                    originalTenantId: current?.OriginalTenantId ?? tenantId,
                    applicationDomain: string.Empty),
                false);
        }

        /// <summary>Strips directory separators and the CR/LF that System.Net.Mail rejects in a header.</summary>
        private static string SafeFileName(string? name, string fallback)
        {
            var candidate = string.IsNullOrWhiteSpace(name) ? fallback : name;

            candidate = candidate
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\\', '_')
                .Replace('/', '_')
                .Trim();

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
        }

        /// <summary>Two attachments sharing a name overwrite one another when the recipient saves them.</summary>
        private static string UniqueName(string name, HashSet<string> usedNames)
        {
            if (usedNames.Add(name))
            {
                return name;
            }

            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);

            for (var suffix = 2; ; suffix++)
            {
                var candidate = $"{stem} ({suffix}){extension}";
                if (usedNames.Add(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
