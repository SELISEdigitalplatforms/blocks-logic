namespace Functions.DomainService.Events
{
    /// <summary>
    /// A tenant's own event on the Genesis bus, read from <c>LogicConstants.FunctionEventQueueName</c> by the event
    /// trigger (<see cref="Consumers.FunctionEventConsumer"/>). The type name is the routing key (Genesis routes by
    /// <c>typeof(T).Name</c>), so it must stay <c>FunctionEvent</c>. The tenant is the bus <c>TenantId</c> property,
    /// not a field.
    /// </summary>
    public class FunctionEvent
    {
        /// <summary>The event id: the dedupe key for any consumer.</summary>
        public string EventId { get; set; } = string.Empty;

        /// <summary>Matches <see cref="FunctionEventCatalog.NamePattern"/>, for example <c>order.created</c>.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The event's data as JSON text (<c>null</c> when there is none).</summary>
        public string DataJson { get; set; } = "null";

        public string SourceFunctionId { get; set; } = string.Empty;

        public string SourceRunId { get; set; } = string.Empty;

        /// <summary>How many events deep this chain is; over <see cref="FunctionEventDepth.MaxDepth"/> it is not run.</summary>
        public int Depth { get; set; }
    }
}
