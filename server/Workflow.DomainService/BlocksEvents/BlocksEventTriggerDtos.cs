using Blocks.Genesis;

namespace Workflow.DomainService.BlocksEvents
{
    /// <summary>One catalog event as the Event Triggers page shows it.</summary>
    public sealed class BlocksEventCatalogItemDto
    {
        public string EventName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ServiceKey { get; set; } = string.Empty;
        public string[] SourceServiceKeys { get; set; } = [];
        public string[] ConsumerNames { get; set; } = [];
        public bool IsValidForWorkflow { get; set; }
        public bool IsValidForFunction { get; set; }

        public static BlocksEventCatalogItemDto From(BlocksEventCatalogEntry entry) => new()
        {
            EventName = entry.EventName,
            Label = entry.Label,
            Description = entry.Description,
            ServiceKey = entry.ServiceKey,
            SourceServiceKeys = entry.SourceServiceKeys ?? [],
            ConsumerNames = entry.ConsumerNames ?? [],
            IsValidForWorkflow = entry.IsValidForWorkflow,
            IsValidForFunction = entry.IsValidForFunction,
        };
    }

    /// <summary><c>GetAll</c>: every catalog event, or <c>IsSeeded=false</c> with no items when the tenant has no catalog.</summary>
    public sealed class BlocksEventCatalogResponse
    {
        public bool IsSeeded { get; set; }
        public List<BlocksEventCatalogItemDto> Items { get; set; } = [];
    }

    /// <summary>One event the Blocks Event node may listen to (enabled for workflows).</summary>
    public sealed class BlocksEventWorkflowOptionDto
    {
        public string EventName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ServiceKey { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class BlocksEventWorkflowEventsResponse
    {
        public List<BlocksEventWorkflowOptionDto> Items { get; set; } = [];
    }

    /// <summary><c>CanManage</c>: returned only to callers with workflow manage permission (anyone else gets 403).</summary>
    public sealed class BlocksEventCanManageResponse
    {
        public bool CanManage { get; set; }
    }

    public sealed class BlocksEventUsageResponse
    {
        public long PublishedWorkflowCount { get; set; }
    }

    /// <summary><c>Update</c>: one event's flags. At least one flag is required; the other is left as it is.</summary>
    public sealed class BlocksEventFlagUpdateRequest
    {
        public string? EventName { get; set; }
        public bool? IsValidForWorkflow { get; set; }
        public bool? IsValidForFunction { get; set; }
    }

    public sealed class BlocksEventFlagUpdateResponse : BaseMutationResponse
    {
        /// <summary>Published workflows that use the event; computed only when the request turns Workflow off.</summary>
        public long AffectedPublishedWorkflowCount { get; set; }
    }

    public static class BlocksEventTriggerErrors
    {
        public const string EventNameRequired = "EVENT_NAME_REQUIRED";
        public const string EventNameTooLong = "EVENT_NAME_TOO_LONG";
        public const string NoFlagProvided = "NO_FLAG_PROVIDED";
        public const string CatalogNotSeeded = "CATALOG_NOT_SEEDED";
        public const string EventNotInCatalog = "EVENT_NOT_IN_CATALOG";
        public const int MaxEventNameLength = 128;
    }
}
