using DomainService.Workflow.Nodes.TriggerEmailV1;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Events
{

    public enum EmailTriggerType
    {
        Inbound,
        Outbound
    }

    [ExcludeFromCodeCoverage]
    public record EmailTriggerEvent
    {
        public required EmailTriggerType Type { get; set; }
        public required string ProjectKey { get; set; }
        public required EmailBox Mail { get; set; }
    }

}

