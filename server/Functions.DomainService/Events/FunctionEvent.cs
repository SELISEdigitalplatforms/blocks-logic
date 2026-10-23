namespace Functions.DomainService.Events
{
    /// <summary>
    /// A tenant's own event, sent by a function with <c>ctx.events.add('FunctionEvent', { name, data })</c>. Travels the
    /// Genesis bus to <c>LogicConstants.FunctionEventQueueName</c>. The type name is the routing key (Genesis routes by
    /// <c>typeof(T).Name</c>), so it must stay <c>FunctionEvent</c>.
    /// <para>
    /// Everything here is set by the platform from the run record and the checked payload, never from the function's own
    /// claim about itself: the tenant is the bus <c>TenantId</c> property, not a field.
    /// </para>
    /// </summary>
    public class FunctionEvent
    {
        /// <summary>The event id (<c>{runId}-{attempt}-{n}</c>): the dedupe key for any consumer.</summary>
        public string EventId { get; set; } = string.Empty;

        /// <summary>Matches <see cref="FunctionEventCatalog.NamePattern"/>, for example <c>order.created</c>.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The event's data as JSON text (<c>null</c> when the function sent none).</summary>
        public string DataJson { get; set; } = "null";

        public string SourceFunctionId { get; set; } = string.Empty;

        public string SourceRunId { get; set; } = string.Empty;

        /// <summary>How many events deep this chain is: 0 for a run not started by an event (see <see cref="FunctionEventDepth"/>).</summary>
        public int Depth { get; set; }
    }
}
