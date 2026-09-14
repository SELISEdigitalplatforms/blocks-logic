using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Protocol;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Status mapping decides what a tenant is told and what the Worker records. The ordering
    /// rules are the point: the container's fate outranks anything it printed.
    /// </summary>
    public class RunOutcomeTests
    {
        private static SandboxOutput Empty() => new();

        private static SandboxOutput Succeeded()
        {
            var parser = new SandboxOutputParser();
            return parser.Parse("""{"t":"result","ok":true,"value":1}""" + "\n");
        }

        private static SandboxOutput FailedWith(string code)
        {
            var parser = new SandboxOutputParser();
            return parser.Parse($$"""{"t":"result","ok":false,"code":"{{code}}","message":"m"}""" + "\n");
        }

        [Fact]
        public void An_oom_kill_outranks_a_cheerful_result_line()
        {
            // A sandbox can print whatever it likes before dying. The kill is the fact.
            var (status, code, _) = RunOutcome.Map(
                oomKilled: true, exitCode: 137, timedOut: false, cancelled: false, Succeeded());

            status.Should().Be(RunStatuses.ResourceExceeded);
            code.Should().Be(ErrorCodes.MemoryLimit);
        }

        [Fact]
        public void A_cancel_outranks_a_timeout()
        {
            var (status, _, _) = RunOutcome.Map(false, 137, timedOut: true, cancelled: true, Empty());
            status.Should().Be(RunStatuses.Cancelled);
        }

        [Fact]
        public void A_hard_timeout_is_reported_as_timed_out()
        {
            var (status, code, _) = RunOutcome.Map(false, 137, timedOut: true, cancelled: false, Empty());
            status.Should().Be(RunStatuses.TimedOut);
            code.Should().Be(ErrorCodes.TimedOut);
        }

        [Fact]
        public void A_clean_result_succeeds()
        {
            var (status, code, _) = RunOutcome.Map(false, 0, false, false, Succeeded());
            status.Should().Be(RunStatuses.Succeeded);
            code.Should().BeNull();
        }

        [Fact]
        public void The_sandboxs_own_timeout_maps_to_timed_out_not_failed()
        {
            var (status, code, _) = RunOutcome.Map(false, 10, false, false, FailedWith(ErrorCodes.TimedOut));
            status.Should().Be(RunStatuses.TimedOut);
            code.Should().Be(ErrorCodes.TimedOut);
        }

        [Theory]
        [InlineData(ErrorCodes.MemoryLimit)]
        [InlineData(ErrorCodes.PidLimit)]
        public void Resource_codes_map_to_resource_exceeded(string errorCode)
        {
            var (status, _, _) = RunOutcome.Map(false, 10, false, false, FailedWith(errorCode));
            status.Should().Be(RunStatuses.ResourceExceeded);
        }

        [Fact]
        public void A_user_error_is_a_plain_failure()
        {
            var (status, code, _) = RunOutcome.Map(false, 10, false, false, FailedWith(ErrorCodes.UserRuntimeError));
            status.Should().Be(RunStatuses.Failed);
            code.Should().Be(ErrorCodes.UserRuntimeError);
        }

        [Fact]
        public void Exiting_zero_with_no_result_is_a_failure_not_a_success()
        {
            // Silence is not consent: without a result line there is nothing to return.
            var (status, code, _) = RunOutcome.Map(false, SandboxExit.Ok, false, false, Empty());
            status.Should().Be(RunStatuses.Failed);
            code.Should().Be(ErrorCodes.RuntimeStartFailed);
        }

        [Fact]
        public void Exit_20_is_attributed_to_the_runtime_not_the_tenant()
        {
            var (status, code, _) = RunOutcome.Map(false, SandboxExit.BootstrapError, false, false, Empty());
            status.Should().Be(RunStatuses.Failed);
            code.Should().Be(ErrorCodes.RuntimeStartFailed);
        }

        [Fact]
        public void A_bare_sigkill_is_assumed_to_be_the_memory_ceiling()
        {
            var (status, code, _) = RunOutcome.Map(false, SandboxExit.Sigkill, false, false, Empty());
            status.Should().Be(RunStatuses.ResourceExceeded);
            code.Should().Be(ErrorCodes.MemoryLimit);
        }

        [Fact]
        public void Sigterm_without_a_result_reads_as_cancellation()
        {
            var (status, _, _) = RunOutcome.Map(false, SandboxExit.Sigterm, false, false, Empty());
            status.Should().Be(RunStatuses.Cancelled);
        }
    }
}
