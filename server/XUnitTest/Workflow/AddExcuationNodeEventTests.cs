using System.Text.Json;
using FluentAssertions;
using Workflow.DomainService.Events;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Genesis deserializes the bus payload with default <see cref="JsonSerializer"/> (PascalCase,
    /// C# <c>required</c> ⇒ JSON required). TenantId was added later, so in-flight Azure messages
    /// omit it; deserialize must still succeed so the engine can fill it from BlocksContext.
    /// </summary>
    public class AddExcuationNodeEventTests
    {
        [Fact]
        public void Deserialize_AcceptsPayloadPublishedBeforeTenantId()
        {
            const string json = """{"WorkflowId":"w1","WorkflowExecutionId":"e1","NodeId":"n1"}""";

            var evt = JsonSerializer.Deserialize<AddExcuationNodeEvent>(json);

            evt.Should().NotBeNull();
            evt!.WorkflowId.Should().Be("w1");
            evt.WorkflowExecutionId.Should().Be("e1");
            evt.NodeId.Should().Be("n1");
            evt.TenantId.Should().BeEmpty();
        }

        [Fact]
        public void Deserialize_ReadsTenantIdWhenPublisherSendsIt()
        {
            const string json =
                """{"TenantId":"t1","WorkflowId":"w1","WorkflowExecutionId":"e1","NodeId":"n1"}""";

            var evt = JsonSerializer.Deserialize<AddExcuationNodeEvent>(json);

            evt!.TenantId.Should().Be("t1");
        }
    }
}
