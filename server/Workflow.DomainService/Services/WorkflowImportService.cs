using System.Text;
using Blocks.Genesis;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using StorageDriver;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Import;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Utils;

namespace Workflow.DomainService.Services
{
    public class WorkflowImportService : IWorkflowImportService
    {
        private readonly IMessageClient _messageClient;
        private readonly IStorageDriverService _storageDriverService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWorkflowRepository _workflowRepository;
        private readonly IWorkflowNotificationService _notificationService;
        private readonly IWorkflowImportTenantSlugResolver _tenantSlugResolver;
        private readonly ILogger<WorkflowImportService> _logger;

        public WorkflowImportService(
            IMessageClient messageClient,
            IStorageDriverService storageDriverService,
            IHttpClientFactory httpClientFactory,
            IWorkflowRepository workflowRepository,
            IWorkflowNotificationService notificationService,
            IWorkflowImportTenantSlugResolver tenantSlugResolver,
            ILogger<WorkflowImportService> logger)
        {
            _messageClient = messageClient;
            _storageDriverService = storageDriverService;
            _httpClientFactory = httpClientFactory;
            _workflowRepository = workflowRepository;
            _notificationService = notificationService;
            _tenantSlugResolver = tenantSlugResolver;
            _logger = logger;
        }

        public async Task<BaseMutationResponse> EnqueueAsync(WorkflowImportRequestDto request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.FileId))
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "FileId is required" } },
                };
            }

            var context = BlocksContext.GetContext();
            var tenantId = context?.TenantId ?? string.Empty;
            var userId = context?.UserId ?? string.Empty;

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<WorkflowImportEvent>
            {
                ConsumerName = LogicConstants.WorkflowImportQueue,
                Payload = new WorkflowImportEvent
                {
                    FileId = request.FileId,
                    MessageCoRelationId = request.MessageCoRelationId ?? string.Empty,
                    TenantId = tenantId,
                    UserId = userId,
                },
            });

            return new BaseMutationResponse { IsSuccess = true };
        }

        public async Task ImportAsync(WorkflowImportEvent evt)
        {
            var userIds = string.IsNullOrWhiteSpace(evt.UserId) ? new List<string>() : new List<string> { evt.UserId };
            try
            {
                var (ok, text, error) = await DownloadFileAsync(evt.FileId);
                if (!ok || text is null)
                {
                    await NotifyFailureAsync(userIds, evt.MessageCoRelationId, error ?? "Could not download the import file.");
                    return;
                }

                var size = Encoding.UTF8.GetByteCount(text);
                var preflight = WorkflowImportMapper.Preflight(text, size);
                if (!preflight.Ok)
                {
                    await NotifyFailureAsync(userIds, evt.MessageCoRelationId, preflight.Message);
                    return;
                }

                var remapped = WorkflowImportMapper.RemapAndSanitise(preflight.Root);
                var tenantSlug = await _tenantSlugResolver.ResolveAsync(evt.TenantId);
                WorkflowImportMapper.RewriteProjectIdentity(remapped.Nodes, evt.TenantId, tenantSlug);

                var userId = string.IsNullOrWhiteSpace(evt.UserId) ? "system" : evt.UserId;
                var model = new WorkflowEntity
                {
                    ItemId = Guid.NewGuid().ToString("N"),
                    Name = WorkflowImportMapper.GetName(preflight.Root),
                    TenantId = evt.TenantId,
                    Nodes = remapped.Nodes,
                    Edges = remapped.Edges,
                    Settings = remapped.Settings,
                    IsDirty = true,
                    IsPublished = false,
                    PublishedVersionId = null,
                    PublishedMeta = null,
                    LastPublishedVersionId = null,
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    CreatedBy = userId,
                    LastUpdatedBy = userId,
                };

                await _workflowRepository.CreateWorkflowAsync(model);

                await _notificationService.NotifyImportAsync(
                    userIds,
                    evt.MessageCoRelationId,
                    isSuccess: true,
                    title: "Workflow imported",
                    description: remapped.Issues > 0
                        ? $"Your workflow is ready. {remapped.Issues} item(s) were skipped because they were invalid or disconnected."
                        : "Your workflow is ready.",
                    workflowId: model.ItemId,
                    issues: remapped.Issues);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow import failed for FileId {FileId}", evt.FileId);
                await NotifyFailureAsync(userIds, evt.MessageCoRelationId, "An unexpected error occurred while importing the workflow.");
            }
        }

        private async Task<(bool ok, string? text, string? error)> DownloadFileAsync(string fileId)
        {
            var file = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest { FileId = fileId });
            if (file is null || string.IsNullOrWhiteSpace(file.Url))
            {
                return (false, null, "The import file could not be found.");
            }

            var client = _httpClientFactory.CreateClient(nameof(WorkflowImportService));
            using var response = await client.GetAsync(new Uri(file.Url));
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, "Downloading the import file failed. The pre-signed URL may have expired.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.LongLength > WorkflowImportMapper.MaxImportBytes)
            {
                return (false, null, WorkflowImportMapper.ImportTooLargeMessage);
            }

            return (true, Encoding.UTF8.GetString(bytes), null);
        }

        private Task NotifyFailureAsync(List<string> userIds, string correlationId, string description)
        {
            return _notificationService.NotifyImportAsync(
                userIds,
                correlationId,
                isSuccess: false,
                title: "Workflow import failed",
                description: description,
                workflowId: null,
                issues: 0);
        }
    }
}
