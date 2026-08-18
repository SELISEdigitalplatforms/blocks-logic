using System.Text.Json;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Nodes.TriggerScheduleV1;
using FluentAssertions;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    public class TriggerScheduleV1NodeTests
    {
        private static NodeExecutionContext Context(BsonDocument workflowContext, BsonDocument? parameters = null)
            => new()
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters ?? new BsonDocument
                {
                    { "TriggerInterval", "minutes" },
                    { "CronExpression", "30 */10 * * * *" }
                },
                InputItems = new List<WorkflowItemExecutionEntity>(),
                IterationCount = 0,
                WorkflowContext = workflowContext
            };

        [Fact]
        public async Task RunAsync_EmitsOutputItemPerInput()
        {
            var node = new TriggerScheduleV1Node();
            var input = new BsonArray
            {
                new BsonDocument
                {
                    { "WorkflowId", "wf-1" },
                    { "TriggerId", "node-1" },
                    { "TenantId", "tenant-1" },
                    { "CronExpression", "30 */10 * * * *" },
                    { "FiredAt", "2026-08-18T00:00:00Z" }
                }
            };
            var ctx = Context(new BsonDocument("Input", input));

            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Branch.Should().Be("source");
            result.OutputItems[0].Data.Output["WorkflowId"].AsString.Should().Be("wf-1");
            result.OutputItems[0].Data.Output["TriggerId"].AsString.Should().Be("node-1");
        }

        [Fact]
        public async Task RunAsync_ParametersEchoedInOutput()
        {
            var node = new TriggerScheduleV1Node();
            var input = new BsonArray { new BsonDocument("WorkflowId", "wf-1") };
            var ctx = Context(new BsonDocument("Input", input));

            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems[0].Data.Parameters["CronExpression"].AsString
                .Should().Be("30 */10 * * * *");
            result.OutputItems[0].Data.Parameters["TriggerInterval"].AsString
                .Should().Be("minutes");
        }

        [Fact]
        public async Task RunAsync_EmptyParameters_Tolerated()
        {
            var node = new TriggerScheduleV1Node();
            var input = new BsonArray { new BsonDocument("WorkflowId", "wf-1") };
            var ctx = Context(new BsonDocument("Input", input), parameters: new BsonDocument());

            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(1);
            result.OutputItems[0].Data.Parameters["TriggerInterval"].AsString
                .Should().Be("days");
        }

        [Fact]
        public async Task RunAsync_MissingInput_ReturnsFailure()
        {
            var node = new TriggerScheduleV1Node();
            var ctx = Context(new BsonDocument());

            var result = await node.RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = new TriggerScheduleV1Node();
            node.NodeType.Should().Be("schedule");
            node.Version.Should().Be("v1");
        }

        [Fact]
        public async Task ValidateConfigurationAsync_ValidCron_ReturnsTrue()
        {
            using var doc = JsonDocument.Parse("{\"CronExpression\":\"30 */10 * * * *\"}");
            (await TriggerScheduleV1Node.ValidateConfigurationAsync(doc)).Should().BeTrue();
        }

        [Fact]
        public async Task ValidateConfigurationAsync_FiveFieldCron_ReturnsTrue()
        {
            using var doc = JsonDocument.Parse("{\"CronExpression\":\"0 9 * * *\"}");
            (await TriggerScheduleV1Node.ValidateConfigurationAsync(doc)).Should().BeTrue();
        }

        [Fact]
        public async Task ValidateConfigurationAsync_MissingOrInvalidCron_ReturnsFalse()
        {
            using var empty = JsonDocument.Parse("{\"CronExpression\":\"\"}");
            (await TriggerScheduleV1Node.ValidateConfigurationAsync(empty)).Should().BeFalse();

            using var invalid = JsonDocument.Parse("{\"CronExpression\":\"not-a-cron\"}");
            (await TriggerScheduleV1Node.ValidateConfigurationAsync(invalid)).Should().BeFalse();
        }
    }
}
