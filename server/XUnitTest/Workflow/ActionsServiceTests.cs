using Blocks.Genesis;
using FluentAssertions;
using Moq;
using Workflow.DomainService.Actions;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The editors' pickers (<c>Actions/GetForWorkflow</c>, <c>Actions/GetForFunction</c>) list only the tenant's actions
    /// whose switch for that target is on, read straight from the tenant's <c>actions</c> KV entry.
    /// </summary>
    public class ActionsServiceTests
    {
        private readonly Mock<IKeyValueStore> _store = new();

        private ActionsService Service() => new(new ActionsRepository(_store.Object));

        private void Stored(List<ActionDefinition>? actions) =>
            _store.Setup(s => s.GetAsync<List<ActionDefinition>>(ActionConstants.ActionsKey, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(actions);

        private static ActionDefinition Action(string name, string service, bool workflow, bool function) => new()
        {
            Name = name,
            Label = name,
            Service = service,
            Payload = new Dictionary<string, string> { ["userId"] = "string" },
            Workflow = workflow,
            Function = function,
        };

        [Fact]
        public async Task Workflow_picker_lists_only_actions_with_the_workflow_switch_on_sorted_by_service()
        {
            Stored(
            [
                Action("localization.files.generated", "localization", workflow: true, function: false),
                Action("iam.user.updated", "iam", workflow: false, function: true),
                Action("iam.user.created", "iam", workflow: true, function: false),
            ]);

            var options = await Service().GetForWorkflowAsync();

            options.Select(o => o.Name).Should().Equal("iam.user.created", "localization.files.generated");
            options[0].Payload.Should().ContainKey("userId");
        }

        [Fact]
        public async Task Function_picker_lists_only_actions_with_the_function_switch_on()
        {
            Stored(
            [
                Action("iam.user.created", "iam", workflow: true, function: false),
                Action("iam.user.updated", "iam", workflow: false, function: true),
            ]);

            (await Service().GetForFunctionAsync()).Select(o => o.Name).Should().Equal("iam.user.updated");
        }

        [Fact]
        public async Task A_tenant_without_an_actions_entry_gets_empty_lists()
        {
            Stored(null);

            (await Service().GetForWorkflowAsync()).Should().BeEmpty();
            (await Service().GetForFunctionAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Find_matches_the_name_exactly()
        {
            Stored([Action("iam.user.created", "iam", workflow: true, function: true)]);
            var repository = new ActionsRepository(_store.Object);

            (await repository.FindAsync("iam.user.created")).Should().NotBeNull();
            (await repository.FindAsync("IAM.user.created")).Should().BeNull();
        }
    }
}
