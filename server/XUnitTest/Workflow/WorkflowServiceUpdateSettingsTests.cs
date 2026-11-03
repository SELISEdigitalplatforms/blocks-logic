using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Services;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using Workflow.DomainService.Validators;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>blocks-logic#380: PUT UpdateSettings validates an enabled alert and saves only the settings.</summary>
    public class WorkflowServiceUpdateSettingsTests : IDisposable
    {
        private const string Tenant = "tenant-wf";
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly WorkflowService _service;

        public WorkflowServiceUpdateSettingsTests()
        {
            TestBlocksContext.Set(Tenant, "user-wf");
            _workflows.Setup(r => r.UpdateWorkflowSettingsAsync(Tenant, "wf-a", It.IsAny<WorkflowSettings>(), "user-wf")).ReturnsAsync(true);
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-b")).ReturnsAsync(Target("wf-b", published: true, LogicConstants.ErrorTriggerNodeType));
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-c")).ReturnsAsync(Target("wf-c", published: true, "webhook"));
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-d")).ReturnsAsync(Target("wf-d", published: false, LogicConstants.ErrorTriggerNodeType));
            // Published without an Error Trigger; one was added to the draft afterwards.
            _workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-e")).ReturnsAsync(Target("wf-e", published: true, "webhook", draftTriggerType: LogicConstants.ErrorTriggerNodeType));
            _service = new WorkflowService(_workflows.Object, Mock.Of<IWorkflowVersionRepository>(), Mock.Of<IScheduleService>(),
                Mock.Of<ILogger<WorkflowService>>(), Mock.Of<global::Workflow.DomainService.BlocksEvents.IBlocksEventCatalogService>(),
                new WorkflowUpdateSettingsValidator(_workflows.Object));
        }

        public void Dispose() => TestBlocksContext.Clear();

        // Published: the trigger is in the published version. Unpublished: in the draft.
        private static WorkflowEntity Target(string id, bool published, string triggerType, string? draftTriggerType = null)
        {
            static NodeEntity Trigger(string type) => new() { Id = "t", Name = "Trigger", Version = "v2", Type = type, Category = "trigger", Position = new Position() };
            var trigger = Trigger(triggerType);
            var draftTrigger = Trigger(draftTriggerType ?? triggerType);
            return new()
            {
                TenantId = Tenant,
                ItemId = id,
                IsPublished = published,
                PublishedVersionId = published ? "ver" : null,
                Nodes = [draftTrigger],
                PublishedMeta = published ? new PublishedWorkflowMeta { TriggerNodes = [trigger] } : null,
            };
        }

        private static WorkflowUpdateSettingsRequestDto Request(bool isEnabled, string? target, WorkflowAlertMode mode = WorkflowAlertMode.Workflow) => new()
        {
            ItemId = "wf-a",
            Settings = new WorkflowSettings { Alert = new WorkflowAlert { IsEnabled = isEnabled, Mode = mode, WorkflowId = target } },
        };

        [Fact]
        public async Task H5_AValidEnabledAlert_IsSavedWithATargetedUpdate_AndNothingElse()
        {
            var request = Request(true, "wf-b");

            var result = await _service.UpdateSettingsAsync(Tenant, request);

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("wf-a");
            _workflows.Verify(r => r.UpdateWorkflowSettingsAsync(Tenant, "wf-a", request.Settings!, "user-wf"), Times.Once);
            _workflows.Verify(r => r.UpdateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
        }

        [Fact]
        public async Task AnUnpublishedWorkflow_WithAnErrorTriggerInItsDraft_CanBeChosen()
        {
            var result = await _service.UpdateSettingsAsync(Tenant, Request(true, "wf-d"));

            result.IsSuccess.Should().BeTrue();
            _workflows.Verify(r => r.UpdateWorkflowSettingsAsync(Tenant, "wf-a", It.IsAny<WorkflowSettings>(), "user-wf"), Times.Once);
        }

        [Fact]
        public async Task H7_ADisabledAlert_IsSavedWithoutChecks_KeepingItsTarget()
        {
            var result = await _service.UpdateSettingsAsync(Tenant, Request(false, "wf-gone"));

            result.IsSuccess.Should().BeTrue();
            _workflows.Verify(r => r.UpdateWorkflowSettingsAsync(Tenant, "wf-a",
                It.Is<WorkflowSettings>(s => !s.Alert!.IsEnabled && s.Alert.WorkflowId == "wf-gone"), "user-wf"), Times.Once);
        }

        [Fact]
        public async Task NullSettings_AreSavedAsEmptySettings()
        {
            var result = await _service.UpdateSettingsAsync(Tenant, new WorkflowUpdateSettingsRequestDto { ItemId = "wf-a" });

            result.IsSuccess.Should().BeTrue();
            _workflows.Verify(r => r.UpdateWorkflowSettingsAsync(Tenant, "wf-a", It.Is<WorkflowSettings>(s => s.Alert == null), "user-wf"), Times.Once);
        }

        [Theory]
        [InlineData(null, 1, "Settings.Alert.WorkflowId", "Select a workflow.")]
        [InlineData(" ", 1, "Settings.Alert.WorkflowId", "Select a workflow.")]
        [InlineData("wf-a", 1, "Settings.Alert.WorkflowId", "A workflow can't handle its own failures.")]
        [InlineData("wf-x", 1, "Settings.Alert.WorkflowId", "Workflow not found.")]
        [InlineData("wf-e", 1, "Settings.Alert.WorkflowId", "The selected workflow has no Error Trigger.")]
        [InlineData("wf-c", 1, "Settings.Alert.WorkflowId", "The selected workflow has no Error Trigger.")]
        [InlineData("wf-b", 2, "Settings.Alert.Mode", "Unsupported alert mode.")]
        public async Task C9_AnInvalidEnabledAlert_IsRefused_AndNothingIsStored(string? target, int mode, string key, string message)
        {
            var result = await _service.UpdateSettingsAsync(Tenant, Request(true, target, (WorkflowAlertMode)mode));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { [key] = message });
            _workflows.Verify(r => r.UpdateWorkflowSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkflowSettings>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C11_AWorkflowThatIsNotFoundOrNotEditable_IsReported()
        {
            _workflows.Setup(r => r.UpdateWorkflowSettingsAsync(Tenant, "wf-a", It.IsAny<WorkflowSettings>(), "user-wf")).ReturnsAsync(false);

            var result = await _service.UpdateSettingsAsync(Tenant, Request(false, null));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Message");
        }
    }
}
