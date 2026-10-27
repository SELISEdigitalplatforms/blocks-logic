using System.ComponentModel.DataAnnotations;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Settings the agreed runner flow bounds (RUNNER-FLOW steps 0 and 5.4). The host refuses to start
    /// with a value outside them (ValidateOnStart), so a typo in runner.env cannot quietly widen them.
    /// </summary>
    public sealed class RunnerOptionsRangeTests
    {
        private static bool IsValid(RunnerOptions options) =>
            Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);

        [Fact]
        public void The_kill_grace_after_the_timeout_is_at_most_five_seconds()
        {
            IsValid(new RunnerOptions()).Should().BeTrue("the default 2 s stays");
            IsValid(new RunnerOptions { KillGraceSeconds = 5 }).Should().BeTrue();
            IsValid(new RunnerOptions { KillGraceSeconds = 6 }).Should().BeFalse();
        }

        [Fact]
        public void The_reserved_cores_setting_is_between_one_and_two()
        {
            IsValid(new RunnerOptions { ReservedCores = 1.5 }).Should().BeTrue();
            IsValid(new RunnerOptions { ReservedCores = 0.5 }).Should().BeFalse();
            IsValid(new RunnerOptions { ReservedCores = 3 }).Should().BeFalse();
        }
    }
}
