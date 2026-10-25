using System.Net;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;

namespace XUnitTest.TestHelpers
{
    /// <summary>A real <see cref="WorkflowOutboundGuard"/> whose DNS answers one public address, so no test needs a network.</summary>
    internal static class TestOutboundGuard
    {
        public static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

        public static WorkflowOutboundGuard ResolvingTo(params IPAddress[] addresses) =>
            new((_, _) => Task.FromResult(addresses.Length > 0 ? addresses : [PublicAddress]),
                WorkflowOutboundGuard.IsBlockedAddress);
    }
}
