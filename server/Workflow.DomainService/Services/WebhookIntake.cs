using System.Text.Json;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Workflow.DomainService.Services
{
    /// <summary>The trigger items an Any content request produced, and what to undo if the run cannot start.</summary>
    public sealed class WebhookIntakeResult
    {
        public required BsonArray Items { get; init; }
        public required string BodyFormat { get; init; }
        public long Bytes { get; init; }
        public IReadOnlyList<string> StoredFileIds { get; init; } = [];
        public WebhookFileTarget? Target { get; init; }
    }

    public interface IWebhookIntake
    {
        /// <summary>Legacy mode: a JSON object or array of objects, exactly as before.</summary>
        Task<BsonArray> ReadLegacyAsync(HttpRequest request, CancellationToken ct = default);

        /// <summary>Any content mode: the item wrapper, with every file part or binary body stored first.</summary>
        Task<WebhookIntakeResult> ReadAnyContentAsync(
            HttpRequest request, WebhookIntakeSettings settings, string tenantId, BlocksContext? caller, CancellationToken ct = default);

        /// <summary>Deletes the files of a result whose execution could not be created.</summary>
        Task DiscardAsync(WebhookIntakeResult result, CancellationToken ct = default);
    }

    /// <summary>
    /// Turns a webhook request into trigger items (SPEC8). Files are stored one at a time in request order; if any
    /// fails, those already stored for the request are deleted and no item is returned (all-or-nothing).
    /// </summary>
    public sealed class WebhookIntake : IWebhookIntake
    {
        private readonly IWebhookFileStore _fileStore;
        private readonly ILogger<WebhookIntake> _logger;

        public WebhookIntake(IWebhookFileStore fileStore, ILogger<WebhookIntake> logger)
        {
            _fileStore = fileStore;
            _logger = logger;
        }

        public async Task<BsonArray> ReadLegacyAsync(HttpRequest request, CancellationToken ct = default)
        {
            JsonElement input = await WebhookContentReader.ReadLegacyJsonAsync(request, ct);
            return WebhookBodyNormalizer.Normalize(input);
        }

        public async Task<WebhookIntakeResult> ReadAnyContentAsync(
            HttpRequest request, WebhookIntakeSettings settings, string tenantId, BlocksContext? caller, CancellationToken ct = default)
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(request, settings, ct);

            var target = new WebhookFileTarget
            {
                TenantId = tenantId,
                Caller = caller,
                FolderId = string.IsNullOrWhiteSpace(settings.FilesFolderId) ? null : settings.FilesFolderId,
                ConfigurationName = string.IsNullOrWhiteSpace(settings.StorageConfigurationName) ? null : settings.StorageConfigurationName,
            };

            var stored = new List<string>(content.Files.Count);
            var references = new BsonArray();
            try
            {
                foreach (var file in content.Files)
                {
                    var fileId = await _fileStore.StoreAsync(file, target, ct);
                    stored.Add(fileId);
                    references.Add(new BsonDocument
                    {
                        { "field", file.Field },
                        { "fileId", fileId },
                        { "name", file.Name },
                        { "contentType", file.ContentType },
                        { "size", file.Length },
                    });
                }
            }
            catch
            {
                await DeleteAsync(stored, target);
                throw;
            }

            var items = new BsonArray(content.Bodies.Select(body => new BsonDocument
            {
                { "body", body },
                { "files", references.DeepClone() },
                { "contentType", content.ContentType },
                { "bodyFormat", content.BodyFormat },
                { "headers", content.Headers.DeepClone() },
                { "query", content.Query.DeepClone() },
            }));

            return new WebhookIntakeResult
            {
                Items = items,
                BodyFormat = content.BodyFormat,
                Bytes = content.Bytes,
                StoredFileIds = stored,
                Target = target,
            };
        }

        public Task DiscardAsync(WebhookIntakeResult result, CancellationToken ct = default) =>
            result.Target is null ? Task.CompletedTask : DeleteAsync(result.StoredFileIds, result.Target);

        /// <summary>Not cancellable: the rollback runs even when the request was aborted.</summary>
        private async Task DeleteAsync(IReadOnlyCollection<string> fileIds, WebhookFileTarget target)
        {
            var failed = 0;
            foreach (var fileId in fileIds)
            {
                if (!await _fileStore.DeleteAsync(fileId, target, CancellationToken.None)) failed++;
            }
            if (failed > 0)
            {
                _logger.LogWarning("Webhook intake rollback: {Failed} of {Count} stored file(s) could not be deleted.", failed, fileIds.Count);
            }
        }
    }
}
