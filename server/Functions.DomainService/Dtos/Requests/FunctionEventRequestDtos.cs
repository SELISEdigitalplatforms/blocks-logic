namespace Functions.DomainService.Dtos.Requests
{
    /// <summary>Saves the event settings of a function's draft: the event names that start it.</summary>
    public sealed class SaveEventSettingsRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;

        /// <summary>Action names that start this function (the Action trigger). Each must be in the tenant's actions.</summary>
        public List<string> TriggerEvents { get; set; } = [];

        /// <summary>The Actions switch. Missing in the request means on.</summary>
        public bool ActionsEnabled { get; set; } = true;
    }
}
