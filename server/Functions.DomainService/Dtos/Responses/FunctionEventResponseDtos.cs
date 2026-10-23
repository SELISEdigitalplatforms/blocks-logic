using System.Text.Json;
using Functions.DomainService.Dtos.Requests;

namespace Functions.DomainService.Dtos.Responses
{
    public sealed class FunctionEventCatalogItemDto
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool PublicAllowed { get; set; }

        /// <summary>The payload as the editor fills it in.</summary>
        public JsonElement PayloadTemplate { get; set; }
    }

    public sealed class FunctionEventCatalogDto
    {
        public List<FunctionEventCatalogItemDto> Items { get; set; } = [];
    }

    /// <summary>The events of a function's draft; empty when none were saved.</summary>
    public sealed class FunctionEventSettingsDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public List<FunctionEventSettingDto> Events { get; set; } = [];
        public List<string> TriggerEvents { get; set; } = [];
    }

    public sealed class RunEventDto
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;

        /// <summary>The name of a <c>FunctionEvent</c>; empty for other types.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary><c>Sent</c>, <c>Failed</c> or <c>Rejected</c>.</summary>
        public string Status { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int Attempts { get; set; }
        public DateTime? SentAt { get; set; }
    }

    /// <summary>What happened to the events of a run's newest attempt that has any. Never a payload.</summary>
    public sealed class RunEventsDto
    {
        public List<RunEventDto> Items { get; set; } = [];

        /// <summary>Why the runner dropped all events of the run (<c>EVENTS_REJECTED: …</c>); null otherwise.</summary>
        public string? Rejected { get; set; }
    }
}
