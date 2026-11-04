using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC11 D4: Storage download URLs are hidden in execution views and kept in the saved items.</summary>
    public class ExecutionUrlMaskerTests
    {
        private const string Url = "https://storage.example/f?sig=SECRET";
        private static readonly HashSet<string> StorageNodes = new(StringComparer.Ordinal) { "get" };

        private static WorkflowItemExecutionEntity Item(string id, string nodeId, BsonDocument output, BsonDocument? input = null,
            BsonDocument? parameters = null) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = nodeId,
            NodeExecutionId = $"ne-{nodeId}",
            NodeName = nodeId,
            Branch = "source",
            ParentItemIds = [],
            AncestorMap = [],
            Data = new NodeOutputItemData { Input = input ?? new BsonDocument(), Output = output, Parameters = parameters ?? new BsonDocument() },
        };

        private static List<WorkflowItemExecutionEntity> Run() =>
        [
            Item("i1", "get", new BsonDocument { { "fileId", "f1" }, { "url", Url }, { "urlExpiresAt", "2026-11-04T10:15:00Z" } }),
            Item("i2", "http", new BsonDocument { { "links", new BsonArray { new BsonDocument("href", Url), "other" } } },
                input: new BsonDocument { { "fileId", "f1" }, { "url", Url } },
                parameters: new BsonDocument("target", Url)),
            Item("i3", "set", new BsonDocument("note", "nothing here")),
        ];

        [Fact]
        public void Masks_the_url_everywhere_it_appears_in_the_execution()   // H11
        {
            var masked = ExecutionUrlMasker.Mask(Run(), StorageNodes);

            masked[0].Data.Output["url"].AsString.Should().Be(ExecutionUrlMasker.Redacted);
            masked[0].Data.Output["urlExpiresAt"].AsString.Should().Be("2026-11-04T10:15:00Z");
            masked[0].Data.Output["fileId"].AsString.Should().Be("f1");
            masked[1].Data.Input["url"].AsString.Should().Be(ExecutionUrlMasker.Redacted);
            masked[1].Data.Parameters.AsBsonDocument["target"].AsString.Should().Be(ExecutionUrlMasker.Redacted);
            masked[1].Data.Output["links"][0]["href"].AsString.Should().Be(ExecutionUrlMasker.Redacted);
            masked[1].Data.Output["links"][1].AsString.Should().Be("other");
            masked.ToJson().Should().NotContain("SECRET");
        }

        [Fact]
        public void Items_without_a_match_are_returned_as_they_are()
        {
            var items = Run();

            var masked = ExecutionUrlMasker.Mask(items, StorageNodes);

            masked[2].Should().BeSameAs(items[2]);
            (masked[1].Id, masked[1].NodeId, masked[1].NodeExecutionId).Should().Be((items[1].Id, items[1].NodeId, items[1].NodeExecutionId));
            masked[1].ParentItemIds.Should().Equal(items[1].ParentItemIds);
        }

        [Fact]
        public void The_saved_items_are_not_changed()   // C15, H12
        {
            var items = Run();

            ExecutionUrlMasker.Mask(items, StorageNodes);

            items[0].Data.Output["url"].AsString.Should().Be(Url);
            items[1].Data.Input["url"].AsString.Should().Be(Url);
        }

        [Fact]
        public void A_url_field_on_another_node_type_is_not_treated_as_a_storage_url()
        {
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", "http", new BsonDocument("url", "https://example.com/public")) };

            ExecutionUrlMasker.Mask(items, StorageNodes).Should().BeSameAs(items);
        }

        [Fact]
        public void Without_storage_nodes_the_list_is_returned_unchanged()
        {
            var items = Run();

            ExecutionUrlMasker.Mask(items, new HashSet<string>()).Should().BeSameAs(items);
        }

        [Fact]
        public void Storage_node_ids_come_from_the_snapshot_types()
        {
            ExecutionUrlMasker.StorageNodeIds([("a", "storage"), ("b", "httpRequest"), ("c", "storage")])
                .Should().BeEquivalentTo(["a", "c"]);
        }

        // ---------------------------------------------------------------- the two execution views

        private static WorkflowExecutionEntity Execution() => new()
        {
            Id = "exec-1",
            TenantId = "tenant-1",
            WorkflowId = "wf-1",
            WorkflowName = "wf",
            TriggerMetadata = new TriggerMetadata(),
            WorkflowSnapshot = new WorkflowEntity
            {
                ItemId = "wf-1",
                Name = "wf",
                TenantId = "tenant-1",
                Nodes =
                [
                    new NodeEntity { Id = "get", Name = "Get", Category = "action", Type = "storage", Version = "v1", Position = new Position() },
                    new NodeEntity { Id = "http", Name = "HTTP", Category = "action", Type = "httpRequest", Version = "v1", Position = new Position() },
                ],
            },
        };

        private static (WorkflowExecutionService Service, Mock<IWorkflowExecutionRepository> Executions) Service()
        {
            var executions = new Mock<IWorkflowExecutionRepository>();
            executions.Setup(r => r.GetByIdForViewAsync("exec-1", "tenant-1")).ReturnsAsync(Execution());
            executions.Setup(r => r.GetLastCompletedExecution("tenant-1", "wf-1")).ReturnsAsync(Execution());
            executions.Setup(r => r.GetAllItemsByExecutionIdAsync("exec-1", "tenant-1")).ReturnsAsync(Run());
            var service = new WorkflowExecutionService(
                new WorkflowExecutionStores(Mock.Of<IWorkflowRepository>(), executions.Object, Mock.Of<IWorkflowVersionRepository>()),
                Mock.Of<IMessageClient>(),
                Mock.Of<ILogger<WorkflowExecutionService>>(),
                Mock.Of<IWorkflowEngineService>(),
                Mock.Of<IWorkflowNotificationService>(),
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                new WorkflowTriggerAccess(Mock.Of<IWorkflowAuthService>(), Mock.Of<IDelegationGrantFactory>(),
                    Mock.Of<IInProcessGrantReleaser>(), Mock.Of<IWebhookIntake>()));
            return (service, executions);
        }

        [Fact]
        public async Task The_execution_view_masks_the_url()   // H11
        {
            var (service, _) = Service();

            var response = await service.GetExecutionByIdAsync("tenant-1", new() { ExecutionId = "exec-1" });

            var json = System.Text.Json.JsonSerializer.Serialize(response.Data);
            json.Should().NotContain("SECRET").And.Contain(ExecutionUrlMasker.Redacted);
        }

        [Fact]
        public async Task The_latest_execution_view_masks_the_url_with_one_item_read()   // H11, C15
        {
            var (service, executions) = Service();

            var response = await service.LastSuccessfullExecutionAsync("tenant-1", new() { WorkflowId = "wf-1" });

            System.Text.Json.JsonSerializer.Serialize(response.Data).Should().NotContain("SECRET");
            executions.Verify(r => r.GetAllItemsByExecutionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }
    }
}
