namespace Functions.DomainService.Dtos.Requests
{
    /// <summary>Saves the event settings of a function's draft: the event names that start it.</summary>
    public sealed class SaveEventSettingsRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;

        /// <summary><c>FunctionEvent</c> names that start this function (used by the event trigger).</summary>
        public List<string> TriggerEvents { get; set; } = [];
    }
}
