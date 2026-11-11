using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// "Test step" saves the trigger's data (pinned, or the Error Trigger's sample) in the run's Input before the
    /// engine reloads the run; unsaved, the trigger failed with "Element 'Input' not found".
    /// </summary>
    public class StepExecuteTriggerInputTests
    {
        private const string Tenant = "tenant-1";
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowEngineService> _engine = new();
        private readonly WorkflowExecutionService _sut;
        private BsonValue? _inputWhenTheEngineRan;

        public StepExecuteTriggerInputTests()
        {
            TestBlocksContext.Set(Tenant, "user-1");
            WorkflowExecutionEntity? saved = null;
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => e);
            _executions.Setup(r => r.UpdateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .Callback((WorkflowExecutionEntity e) => saved = e)
                .Returns(Task.CompletedTask);
            _engine.Setup(e => e.GetTopologicalAncestorsAndTarget(It.IsAny<WorkflowEntity>(), It.IsAny<string>()))
                .Returns((WorkflowEntity w, string _) => w.Nodes);
            _engine.Setup(e => e.ExecuteStepNodeAsync(Tenant, It.IsAny<string>(), "err", "err", null))
                .Callback(() => _inputWhenTheEngineRan = saved?.Context.GetValue(LogicConstants.ExecutionInputKey, BsonNull.Value))
                .ReturnsAsync((string _, string id, string _, string _, string? _) => saved);

            _sut = new WorkflowExecutionService(
                new WorkflowExecutionStores(_workflows.Object, _executions.Object, Mock.Of<IWorkflowVersionRepository>()),
                Mock.Of<IMessageClient>(),
                NullLogger<WorkflowExecutionService>.Instance,
                _engine.Object,
                Mock.Of<IWorkflowNotificationService>(),
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                new WorkflowTriggerAccess(Mock.Of<IWorkflowAuthService>(), Mock.Of<IDelegationGrantFactory>(), Mock.Of<IInProcessGrantReleaser>(), Mock.Of<IWebhookIntake>()));
        }

        private void SetWorkflow(BsonArray? pinData) =>
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = Tenant, ItemId = "wf-b", Name = "Ops alerts", Edges = [],
                Nodes =
                [
                    new NodeEntity
                    {
                        Id = "err", Name = "Error Trigger", Type = LogicConstants.ErrorTriggerNodeType, Category = "trigger",
                        Version = "v2", Position = new Position(), PinData = pinData,
                    },
                ],
            });

        private Task<StepExecuteResponseDto> TestStepAsync() =>
            _sut.StepExecuteAsync(Tenant, new StepExecuteRequestDto { WorkflowId = "wf-b", NodeId = "err" });

        [Fact]
        public async Task The_Error_Trigger_sample_is_saved_in_Input_before_the_engine_runs()
        {
            SetWorkflow(pinData: null);

            (await TestStepAsync()).IsSuccess.Should().BeTrue();

            _inputWhenTheEngineRan.Should().NotBeNull();
            _inputWhenTheEngineRan!.AsBsonArray[0]["execution"]["workflowName"].AsString.Should().Be("Example workflow");
        }

        [Fact]
        public async Task Pinned_trigger_data_is_saved_in_Input_before_the_engine_runs()
        {
            SetWorkflow(pinData: new BsonArray { new BsonDocument("execution", new BsonDocument("workflowName", "Sync orders")) });

            (await TestStepAsync()).IsSuccess.Should().BeTrue();

            _inputWhenTheEngineRan!.AsBsonArray[0]["execution"]["workflowName"].AsString.Should().Be("Sync orders");
        }
    }
}
