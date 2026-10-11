using FluentAssertions;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;

namespace XUnitTest.Functions
{
    /// <summary>
    /// What a delete is told about the workflows that call a function. The interesting case is the
    /// failure one: an empty list is what the delete acts on, so it must never stand in for
    /// "could not check".
    /// </summary>
    public class FunctionUsageServiceTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";

        private readonly Mock<IWorkflowRepository> _workflows = new(MockBehavior.Loose);

        private FunctionUsageService Service() =>
            new(_workflows.Object, NullLogger<FunctionUsageService>.Instance);

        private static WorkflowEntity Workflow(string id, string name, bool published) =>
            new() { ItemId = id, Name = name, TenantId = Tenant, IsPublished = published };

        [Fact]
        public async Task Referencing_workflows_come_back_with_their_names_and_state()
        {
            _workflows.Setup(w => w.GetWorkflowsUsingFunctionAsync(Tenant, FunctionId, It.IsAny<int>()))
                .ReturnsAsync([Workflow("wf_1", "Nightly payouts", true), Workflow("wf_2", "Draft", false)]);

            var references = await Service().GetWorkflowReferencesAsync(Tenant, FunctionId);

            references.Should().Equal(
                new FunctionWorkflowReference("wf_1", "Nightly payouts", true),
                new FunctionWorkflowReference("wf_2", "Draft", false));
        }

        [Fact]
        public async Task A_workflow_with_no_name_is_still_identifiable()
        {
            _workflows.Setup(w => w.GetWorkflowsUsingFunctionAsync(Tenant, FunctionId, It.IsAny<int>()))
                .ReturnsAsync([Workflow("wf_1", "  ", false)]);

            var references = await Service().GetWorkflowReferencesAsync(Tenant, FunctionId);

            references.Should().ContainSingle().Which.Name.Should().Be("(unnamed workflow)");
        }

        [Fact]
        public async Task Nothing_referencing_it_is_an_empty_list()
        {
            _workflows.Setup(w => w.GetWorkflowsUsingFunctionAsync(Tenant, FunctionId, It.IsAny<int>()))
                .ReturnsAsync([]);

            (await Service().GetWorkflowReferencesAsync(Tenant, FunctionId)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_failed_lookup_is_raised_rather_than_answered_with_an_empty_list()
        {
            // Swallowing this would turn "the workflow store is unreachable" into "nothing uses
            // this function", and the guard would quietly stop guarding.
            _workflows.Setup(w => w.GetWorkflowsUsingFunctionAsync(Tenant, FunctionId, It.IsAny<int>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = async () => await Service().GetWorkflowReferencesAsync(Tenant, FunctionId);

            await act.Should().ThrowAsync<TimeoutException>();
        }

        [Fact]
        public async Task An_empty_function_id_is_not_asked_about()
        {
            (await Service().GetWorkflowReferencesAsync(Tenant, "  ")).Should().BeEmpty();

            _workflows.Verify(w => w.GetWorkflowsUsingFunctionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }
    }
}
