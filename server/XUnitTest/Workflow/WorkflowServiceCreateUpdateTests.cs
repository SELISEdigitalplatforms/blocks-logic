using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Scheduler.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// CreateAsync and UpdateAsync both map request nodes through NodeMapper. Create used to
    /// 500 on any node with object parameters (Newtonsoft cannot build a BsonDocument).
    /// </summary>
    public class WorkflowServiceCreateUpdateTests : IDisposable
    {
        private readonly Mock<IWorkflowRepository> _workflowRepository = new();
        private readonly Mock<IWorkflowVersionRepository> _workflowVersionRepository = new();
        private readonly Mock<IScheduleService> _scheduleService = new();
        private readonly WorkflowService _service;

        public WorkflowServiceCreateUpdateTests()
        {
            TestBlocksContext.Set("tenant-wf", "user-wf");
            _service = new WorkflowService(
                _workflowRepository.Object,
                _workflowVersionRepository.Object,
                _scheduleService.Object,
                Mock.Of<ILogger<WorkflowService>>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
        }

        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static NodeDto Node(string id, string? parameters = null, string? settings = null) => new()
        {
            Id = id,
            Name = $"Node {id}",
            Category = "action",
            Type = "transformCode",
            Version = "1",
            Position = new Position { X = 10, Y = 20 },
            Parameters = parameters == null ? default : Json(parameters),
            Settings = settings == null ? default : Json(settings),
        };

        private const string RichParameters =
            "{\"k\":\"v\",\"n\":42,\"d\":1.5,\"b\":true,\"z\":null,\"list\":[1,\"two\",{\"x\":3}],\"nested\":{\"a\":{\"b\":\"c\"}}}";

        [Fact]
        public async Task Create_WithNodes_SavesThem()
        {
            WorkflowEntity? captured = null;
            _workflowRepository
                .Setup(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .Callback<WorkflowEntity>(w => captured = w)
                .Returns(Task.CompletedTask);

            var result = await _service.CreateAsync("tenant-wf", new WorkflowCreateRequestDto
            {
                Name = "From CLI",
                Nodes = new List<NodeDto>
                {
                    Node("n1", RichParameters, "{\"retry\":2}"),
                    Node("n2", "{\"k\":\"other\"}", "{}"),
                },
                Edges = new List<EdgeEnity>
                {
                    new() { Id = "e1", Source = "n1", Target = "n2", SourceHandle = "source", TargetHandle = "target" },
                },
            });

            result.IsSuccess.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Nodes.Should().HaveCount(2);
            captured.Nodes[0].Parameters["k"].AsString.Should().Be("v");
            captured.Nodes[0].Parameters["n"].AsInt32.Should().Be(42);
            captured.Nodes[0].Parameters["nested"]["a"]["b"].AsString.Should().Be("c");
            captured.Nodes[0].Settings["retry"].AsInt32.Should().Be(2);
            captured.Nodes[1].Parameters["k"].AsString.Should().Be("other");
            captured.Edges.Should().ContainSingle(e => e.Source == "n1" && e.Target == "n2");
        }

        [Fact]
        public async Task Create_WithoutNodes_SavesEmptyWorkflow()
        {
            WorkflowEntity? captured = null;
            _workflowRepository
                .Setup(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .Callback<WorkflowEntity>(w => captured = w)
                .Returns(Task.CompletedTask);

            var result = await _service.CreateAsync("tenant-wf", new WorkflowCreateRequestDto { Name = "From UI" });

            result.IsSuccess.Should().BeTrue();
            captured!.Nodes.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_WithNullNodes_SavesEmptyWorkflow()
        {
            WorkflowEntity? captured = null;
            _workflowRepository
                .Setup(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .Callback<WorkflowEntity>(w => captured = w)
                .Returns(Task.CompletedTask);

            var result = await _service.CreateAsync("tenant-wf", new WorkflowCreateRequestDto { Name = "Null nodes", Nodes = null });

            result.IsSuccess.Should().BeTrue();
            captured!.Nodes.Should().BeEmpty();
        }

        [Fact]
        public async Task Create_WithNonObjectParameters_FailsWithoutInserting()
        {
            var result = await _service.CreateAsync("tenant-wf", new WorkflowCreateRequestDto
            {
                Name = "Bad",
                Nodes = new List<NodeDto> { Node("bad-node", "[1,2]") },
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Message"].Should().Contain("bad-node").And.Contain("parameters");
            _workflowRepository.Verify(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }

        [Fact]
        public async Task Create_WhenRepositoryThrows_ReturnsGeneralError()
        {
            _workflowRepository
                .Setup(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .ThrowsAsync(new InvalidOperationException("db down"));

            var result = await _service.CreateAsync("tenant-wf", new WorkflowCreateRequestDto
            {
                Name = "Wf",
                Nodes = new List<NodeDto> { Node("n1", "{\"k\":1}") },
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Message"].Should().Be("db down");
        }

        [Fact]
        public async Task Create_ThenGet_ReturnsTheSameNodeJson()
        {
            WorkflowEntity? captured = null;
            _workflowRepository
                .Setup(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .Callback<WorkflowEntity>(w => captured = w)
                .Returns(Task.CompletedTask);
            _workflowRepository
                .Setup(r => r.GetWorkflowAsync("tenant-wf", It.IsAny<string>()))
                .ReturnsAsync(() => captured!);

            const string settings = "{\"retry\":2,\"note\":\"x\"}";
            var create = await _service.CreateAsync("tenant-wf", new WorkflowCreateRequestDto
            {
                Name = "Round trip",
                Nodes = new List<NodeDto> { Node("n1", RichParameters, settings) },
            });
            var get = await _service.GetAsync("tenant-wf", new WorkflowGetRequestDto { WorkflowId = create.ItemId! });

            get.IsSuccess.Should().BeTrue();
            var node = get.data.Nodes.Should().ContainSingle().Subject;
            JsonNode.DeepEquals(JsonNode.Parse(node.Parameters.GetRawText()), JsonNode.Parse(RichParameters)).Should().BeTrue();
            JsonNode.DeepEquals(JsonNode.Parse(node.Settings.GetRawText()), JsonNode.Parse(settings)).Should().BeTrue();
        }

        private static WorkflowEntity ExistingWorkflow() => new()
        {
            ItemId = "wf-1",
            Name = "Existing",
            TenantId = "tenant-wf",
            Nodes = new List<NodeEntity>
            {
                new()
                {
                    Id = "old",
                    Name = "Old",
                    Category = "action",
                    Type = "transformCode",
                    Version = "1",
                    Position = new Position { X = 0, Y = 0 },
                    Parameters = new BsonDocument { { "keep", "me" } },
                },
            },
            Edges = new(),
            Settings = new(),
        };

        private WorkflowEntity SetupUpdate(WorkflowEntity existing)
        {
            _workflowRepository.Setup(r => r.GetWorkflowAsync("tenant-wf", existing.ItemId)).ReturnsAsync(existing);
            _workflowRepository.Setup(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>())).Returns(Task.CompletedTask);
            return existing;
        }

        [Fact]
        public async Task Update_WithNodes_ReplacesThem()
        {
            var workflow = SetupUpdate(ExistingWorkflow());

            var result = await _service.UpdateAsync("tenant-wf", new WorkflowUpdateRequestDto
            {
                ItemId = "wf-1",
                Nodes = new List<NodeDto> { Node("n1", RichParameters, "{\"retry\":2}") },
            });

            result.IsSuccess.Should().BeTrue();
            workflow.Nodes.Should().ContainSingle();
            workflow.Nodes[0].Parameters.Equals(BsonDocument.Parse(RichParameters)).Should().BeTrue();
            workflow.Nodes[0].Settings["retry"].AsInt32.Should().Be(2);
            _workflowRepository.Verify(r => r.UpdateWorkflowAsync(workflow), Times.Once);
        }

        [Fact]
        public async Task Update_WithNodeMissingParameters_StoresEmptyDocuments()
        {
            var workflow = SetupUpdate(ExistingWorkflow());

            var result = await _service.UpdateAsync("tenant-wf", new WorkflowUpdateRequestDto
            {
                ItemId = "wf-1",
                Nodes = new List<NodeDto> { Node("n1") },
            });

            result.IsSuccess.Should().BeTrue();
            workflow.Nodes[0].Parameters.Should().BeEmpty();
            workflow.Nodes[0].Settings.Should().BeEmpty();
        }

        [Fact]
        public async Task Update_WithNullNodes_KeepsExistingNodes()
        {
            var workflow = SetupUpdate(ExistingWorkflow());

            var result = await _service.UpdateAsync("tenant-wf", new WorkflowUpdateRequestDto { ItemId = "wf-1", Name = "Renamed" });

            result.IsSuccess.Should().BeTrue();
            workflow.Name.Should().Be("Renamed");
            workflow.Nodes.Should().ContainSingle(n => n.Id == "old" && n.Parameters["keep"] == "me");
        }

        [Fact]
        public async Task Update_WithNonObjectSettings_FailsWithoutSaving()
        {
            var workflow = SetupUpdate(ExistingWorkflow());

            var result = await _service.UpdateAsync("tenant-wf", new WorkflowUpdateRequestDto
            {
                ItemId = "wf-1",
                Nodes = new List<NodeDto> { Node("n1", "{}", "\"text\"") },
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Message"].Should().Contain("n1").And.Contain("settings");
            workflow.Nodes.Should().ContainSingle(n => n.Id == "old");
            _workflowRepository.Verify(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }
    }
}
