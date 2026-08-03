using System.Text.Json;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Nodes.TriggerDataV1;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class TriggerDataV1NodeTests
    {
        private static NodeExecutionContext Context(BsonDocument workflowContext)
            => new()
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "CollectionName", "Orders" },
                    { "Operation", "Inserted" },
                    { "ProjectKey", "pk" }
                },
                InputItems = new List<WorkflowItemExecutionEntity>(),
                IterationCount = 0,
                WorkflowContext = workflowContext
            };

        [Fact]
        public async Task RunAsync_EmitsOutputItemPerInput()
        {
            var node = new TriggerDataV1Node();
            var input = new BsonArray
            {
                new BsonDocument("id", 1),
                new BsonDocument("id", 2)
            };
            var ctx = Context(new BsonDocument("Input", input));

            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Branch.Should().Be("source");
        }

        [Fact]
        public async Task RunAsync_MissingInput_ReturnsFailure()
        {
            var node = new TriggerDataV1Node();
            var ctx = Context(new BsonDocument());

            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = new TriggerDataV1Node();
            node.NodeType.Should().Be("dataGateway");
            node.Version.Should().Be("v1");
        }

        [Fact]
        public async Task ValidateConfigurationAsync_ValidConfig_ReturnsTrue()
        {
            using var doc = JsonDocument.Parse("{\"CollectionName\":\"Orders\",\"Operation\":\"Inserted\"}");
            (await TriggerDataV1Node.ValidateConfigurationAsync(doc)).Should().BeTrue();
        }

        [Fact]
        public async Task ValidateConfigurationAsync_MissingFields_ReturnsFalse()
        {
            using var doc = JsonDocument.Parse("{\"CollectionName\":\"\",\"Operation\":\"\"}");
            (await TriggerDataV1Node.ValidateConfigurationAsync(doc)).Should().BeFalse();
        }
    }
}
