using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A deploy copies the settings into the version, so a setting saved after the deploy works only after the next one:
    /// <see cref="FunctionService.SettingsChanged"/> must then say the function needs a deploy.
    /// </summary>
    public class FunctionSettingsDirtyTests
    {
        private const string Action = "iam.user.signed_up";
        private static readonly Dictionary<string, FunctionEventSettingsEntity> NoActionRows = [];

        private static FunctionEntity Function() => new()
        {
            ItemId = "fn_1",
            ActiveVersionId = "v1",
            Limits = new FunctionLimits { RequestsPerMinute = 30 },
            Trigger = new TriggerConfig { HttpMethods = ["POST"] },
            Variables = [new VariableBinding { Key = "A", Value = "1" }],
        };

        // What the deploy stores (FunctionDeploymentService): a copy of the function's settings.
        private static FunctionVersionEntity DeployedFrom(FunctionEntity function) => new()
        {
            ItemId = "v1",
            FunctionId = function.ItemId,
            Limits = function.Limits.ClampKeepingConcurrency(),
            Retry = new RetryPolicy(),
            Trigger = new TriggerConfig { HttpMethods = [.. function.Trigger.HttpMethods ?? []] },
            OutputActions = [],
            Variables = [new VariableBinding { Key = "A", Value = "1" }],
        };

        private static Dictionary<string, FunctionEventSettingsEntity> Rows(params FunctionEventSettingsEntity[] rows) =>
            rows.ToDictionary(r => r.ItemId);

        private static FunctionEventSettingsEntity Row(string? versionId, bool? actionsEnabled, params string[] events) => new()
        {
            ItemId = FunctionEventSettingsEntity.IdFor("fn_1", versionId),
            FunctionId = "fn_1",
            VersionId = versionId,
            TriggerEvents = [.. events],
            ActionsEnabled = actionsEnabled,
        };

        [Fact]
        public void Settings_as_deployed_are_not_a_change()
        {
            var function = Function();

            FunctionService.SettingsChanged(function, DeployedFrom(function), NoActionRows).Should().BeFalse();
        }

        [Fact]
        public void No_active_version_is_left_to_the_code_check()
        {
            FunctionService.SettingsChanged(Function(), null, NoActionRows).Should().BeFalse();
        }

        [Fact]
        public void Turning_web_requests_off_needs_a_deploy()
        {
            var function = Function();
            var version = DeployedFrom(function);
            function.Trigger.HttpEnabled = false;

            FunctionService.SettingsChanged(function, version, NoActionRows).Should().BeTrue();
        }

        [Fact]
        public void Turning_workflow_off_needs_a_deploy()
        {
            var function = Function();
            var version = DeployedFrom(function);
            function.Trigger.WorkflowEnabled = false;

            FunctionService.SettingsChanged(function, version, NoActionRows).Should().BeTrue();
        }

        [Fact]
        public void Changed_limits_or_variables_need_a_deploy()
        {
            var function = Function();
            var version = DeployedFrom(function);
            function.Limits.RequestsPerMinute = 60;
            FunctionService.SettingsChanged(function, version, NoActionRows).Should().BeTrue();

            function = Function();
            function.Variables[0].Value = "2";
            FunctionService.SettingsChanged(function, version, NoActionRows).Should().BeTrue();
        }

        [Fact]
        public void An_empty_verb_list_and_no_list_are_the_same_trigger()
        {
            var function = Function();
            function.Trigger.HttpMethods = [];
            var version = DeployedFrom(function);
            version.Trigger.HttpMethods = null;

            FunctionService.SettingsChanged(function, version, NoActionRows).Should().BeFalse();
            function.Trigger.HttpMethods.Should().BeEmpty("the check must not change the entity it reads");
        }

        [Fact]
        public void Turning_actions_off_needs_a_deploy()
        {
            var function = Function();
            var rows = Rows(Row(null, false, Action), Row("v1", true, Action));

            FunctionService.SettingsChanged(function, DeployedFrom(function), rows).Should().BeTrue();
        }

        [Fact]
        public void Choosing_another_action_needs_a_deploy()
        {
            var function = Function();
            var rows = Rows(Row(null, true, "iam.user.deleted"), Row("v1", true, Action));

            FunctionService.SettingsChanged(function, DeployedFrom(function), rows).Should().BeTrue();
        }

        [Fact]
        public void The_same_action_is_not_a_change_and_an_old_row_counts_as_on()
        {
            var function = Function();
            var rows = Rows(Row(null, true, Action), Row("v1", null, Action));

            FunctionService.SettingsChanged(function, DeployedFrom(function), rows).Should().BeFalse();
        }

        [Fact]
        public void An_empty_draft_and_no_version_row_are_the_same()
        {
            var function = Function();
            var rows = Rows(Row(null, true));

            FunctionService.SettingsChanged(function, DeployedFrom(function), rows).Should().BeFalse();
        }
    }
}
