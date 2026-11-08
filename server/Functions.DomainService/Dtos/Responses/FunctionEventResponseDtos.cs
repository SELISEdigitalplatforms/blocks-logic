namespace Functions.DomainService.Dtos.Responses
{
    /// <summary>The event settings of a function's draft; empty when none were saved.</summary>
    public sealed class FunctionEventSettingsDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public List<string> TriggerEvents { get; set; } = [];

        /// <summary>The Actions switch; true when it was never saved.</summary>
        public bool ActionsEnabled { get; set; } = true;
    }

    /// <summary>
    /// How many runs of one function wait to be started: HTTP (and every other kind but events) and function events.
    /// Events are not real time (they go after HTTP runs), so the oldest waiting one shows how far behind they are.
    /// </summary>
    public sealed class QueueStatusDto
    {
        public long HttpQueued { get; set; }
        public long EventQueued { get; set; }

        /// <summary>When the oldest waiting event run was queued (ISO 8601, UTC); null when none waits.</summary>
        public string? OldestEventQueuedAt { get; set; }
    }
}
