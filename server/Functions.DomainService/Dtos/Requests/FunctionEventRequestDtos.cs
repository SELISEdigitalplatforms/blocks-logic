using System.Text.Json;

namespace Functions.DomainService.Dtos.Requests
{
    /// <summary>One event a function may send, with its payload template (a JSON object, camelCase).</summary>
    public sealed class FunctionEventSettingDto
    {
        /// <summary>A type from the event catalog (<c>GetEventCatalog</c>).</summary>
        public string Type { get; set; } = string.Empty;

        public JsonElement Payload { get; set; }
    }

    /// <summary>Saves the events of a function's draft: what it may send, and the event names that start it.</summary>
    public sealed class SaveEventSettingsRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;

        public List<FunctionEventSettingDto> Events { get; set; } = [];

        /// <summary><c>FunctionEvent</c> names that start this function (used by the event trigger).</summary>
        public List<string> TriggerEvents { get; set; } = [];
    }
}
