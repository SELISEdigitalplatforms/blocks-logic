using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Workflow.DomainService.Repositories;

namespace Workflow.DomainService.BlocksEvents
{
    /// <summary>
    /// The Event Triggers page and the Blocks Event node: read the tenant's catalog, switch one event's
    /// workflow or function flag, and tell Genesis producers to drop their cached copy.
    /// </summary>
    public interface IBlocksEventCatalogService
    {
        Task<BlocksEventCatalogResponse> GetAllAsync(string tenantId);
        Task<BlocksEventWorkflowEventsResponse> GetWorkflowEventsAsync(string tenantId);
        Task<bool> IsEnabledForWorkflowAsync(string tenantId, string eventName);
        Task<BlocksEventUsageResponse> GetUsageAsync(string tenantId, string eventName);
        Task<BlocksEventFlagUpdateResponse> UpdateAsync(string tenantId, BlocksEventFlagUpdateRequest request);
    }

    public sealed class BlocksEventCatalogService : IBlocksEventCatalogService
    {
        private readonly IBlocksEventCatalogRepository _catalog;
        private readonly IWorkflowRepository _workflows;
        private readonly ICacheClient _cache;
        private readonly ILogger<BlocksEventCatalogService> _logger;

        public BlocksEventCatalogService(
            IBlocksEventCatalogRepository catalog,
            IWorkflowRepository workflows,
            ICacheClient cache,
            ILogger<BlocksEventCatalogService> logger)
        {
            _catalog = catalog;
            _workflows = workflows;
            _cache = cache;
            _logger = logger;
        }

        public async Task<BlocksEventCatalogResponse> GetAllAsync(string tenantId)
        {
            var entries = await _catalog.GetAsync(tenantId);
            if (entries is null) return new BlocksEventCatalogResponse { IsSeeded = false };
            return new BlocksEventCatalogResponse
            {
                IsSeeded = true,
                Items = Sorted(entries).Select(BlocksEventCatalogItemDto.From).ToList(),
            };
        }

        public async Task<BlocksEventWorkflowEventsResponse> GetWorkflowEventsAsync(string tenantId)
        {
            var entries = await _catalog.GetAsync(tenantId) ?? [];
            return new BlocksEventWorkflowEventsResponse
            {
                Items = Sorted(entries)
                    .Where(e => e.IsValidForWorkflow)
                    .Select(e => new BlocksEventWorkflowOptionDto
                    {
                        EventName = e.EventName,
                        Label = e.Label,
                        ServiceKey = e.ServiceKey,
                        Description = e.Description,
                    })
                    .ToList(),
            };
        }

        public async Task<bool> IsEnabledForWorkflowAsync(string tenantId, string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return false;
            var entries = await _catalog.GetAsync(tenantId);
            return entries?.Any(e => e.EventName == eventName && e.IsValidForWorkflow) == true;
        }

        public async Task<BlocksEventUsageResponse> GetUsageAsync(string tenantId, string eventName) => new()
        {
            PublishedWorkflowCount = string.IsNullOrWhiteSpace(eventName)
                ? 0
                : await _workflows.CountPublishWorkflowsByBlocksEventAsync(tenantId, eventName.Trim()),
        };

        public async Task<BlocksEventFlagUpdateResponse> UpdateAsync(string tenantId, BlocksEventFlagUpdateRequest request)
        {
            var eventName = request.EventName?.Trim() ?? string.Empty;
            if (eventName.Length == 0)
                return Failed("eventName", BlocksEventTriggerErrors.EventNameRequired, eventName);
            if (eventName.Length > BlocksEventTriggerErrors.MaxEventNameLength)
                return Failed("eventName", BlocksEventTriggerErrors.EventNameTooLong, eventName);
            if (request.IsValidForWorkflow is null && request.IsValidForFunction is null)
                return Failed("Message", BlocksEventTriggerErrors.NoFlagProvided, eventName);

            var userId = BlocksContext.GetContext()?.UserId;
            var result = await _catalog.UpdateFlagsAsync(
                tenantId, eventName, request.IsValidForWorkflow, request.IsValidForFunction, userId);

            switch (result)
            {
                case BlocksEventFlagUpdateResult.CatalogNotSeeded:
                    return Failed("Message", BlocksEventTriggerErrors.CatalogNotSeeded, eventName);
                case BlocksEventFlagUpdateResult.EventNotInCatalog:
                    return Failed("eventName", BlocksEventTriggerErrors.EventNotInCatalog, eventName);
            }

            var response = new BlocksEventFlagUpdateResponse { IsSuccess = true, ItemId = eventName };
            if (request.IsValidForWorkflow == false)
                response.AffectedPublishedWorkflowCount = await _workflows.CountPublishWorkflowsByBlocksEventAsync(tenantId, eventName);

            await PublishInvalidationAsync(tenantId);
            return response;
        }

        /// <summary>
        /// Genesis producers drop their cached catalog for the tenant on this message. A failed publish is
        /// only a warning: the change is saved, and the producers' cache expires on its own within its TTL.
        /// </summary>
        private async Task PublishInvalidationAsync(string tenantId)
        {
            try
            {
                await _cache.PublishAsync(BlocksEventTriggerConstants.InvalidationChannel, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Blocks Event catalog saved, but the cache invalidation publish failed; producers pick it up within {Lifetime}.",
                    BlocksEventTriggerConstants.CacheLifetime);
            }
        }

        private static IEnumerable<BlocksEventCatalogEntry> Sorted(IEnumerable<BlocksEventCatalogEntry> entries) =>
            entries
                .OrderBy(e => e.ServiceKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase);

        private static BlocksEventFlagUpdateResponse Failed(string key, string code, string eventName) => new()
        {
            IsSuccess = false,
            ItemId = eventName.Length == 0 ? null : eventName,
            Errors = new Dictionary<string, string> { { key, code } },
        };
    }
}
