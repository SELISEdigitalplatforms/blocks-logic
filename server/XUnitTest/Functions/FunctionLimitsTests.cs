using FluentAssertions;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The ceilings are the platform's promise, mirrored here from the runner's own constants.
    /// This half clamping correctly is a convenience — it means the interface shows the tenant
    /// the numbers that will actually apply — not a security control: the runner clamps again
    /// before it creates a sandbox, so a mistake here cannot widen one.
    /// </summary>
    public class FunctionLimitsTests
    {
        [Fact]
        public void Defaults_for_a_new_function_sit_below_the_ceilings()
        {
            var limits = new FunctionLimits();

            limits.CpuMillicores.Should().Be(100).And.BeLessThan(FunctionLimits.Ceiling.CpuMillicores);
            limits.MemoryMb.Should().Be(192).And.BeLessThan(FunctionLimits.Ceiling.MemoryMb);
            limits.TimeoutSeconds.Should().Be(10).And.BeLessThan(FunctionLimits.Ceiling.TimeoutSeconds);
            limits.Concurrency.Should().Be(2);
        }

        [Fact]
        public void Rate_limits_default_to_unlimited()
        {
            // Null means unlimited, and unlimited is the only value V1 uses.
            var limits = new FunctionLimits();

            limits.RequestsPerMinute.Should().BeNull();
            limits.RequestsPerDay.Should().BeNull();
        }

        [Fact]
        public void A_greedy_request_is_clamped_to_the_ceilings()
        {
            var clamped = new FunctionLimits
            {
                CpuMillicores = 64_000,
                MemoryMb = 65_536,
                TimeoutSeconds = 86_400,
                Concurrency = 500,
            }.Clamp();

            clamped.CpuMillicores.Should().Be(FunctionLimits.Ceiling.CpuMillicores);
            clamped.MemoryMb.Should().Be(FunctionLimits.Ceiling.MemoryMb);
            clamped.TimeoutSeconds.Should().Be(FunctionLimits.Ceiling.TimeoutSeconds);
            clamped.Concurrency.Should().Be(FunctionLimits.Ceiling.MaxConcurrency);
        }

        [Fact]
        public void A_modest_request_is_honoured()
        {
            var clamped = new FunctionLimits
            {
                CpuMillicores = 150,
                MemoryMb = 256,
                TimeoutSeconds = 30,
                Concurrency = 3,
            }.Clamp();

            clamped.CpuMillicores.Should().Be(150);
            clamped.MemoryMb.Should().Be(256);
            clamped.TimeoutSeconds.Should().Be(30);
            clamped.Concurrency.Should().Be(3);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Nonsensical_values_fall_back_to_defaults_rather_than_widening(int value)
        {
            var clamped = new FunctionLimits
            {
                CpuMillicores = value,
                MemoryMb = value,
                TimeoutSeconds = value,
                Concurrency = value,
            }.Clamp();

            clamped.CpuMillicores.Should().Be(FunctionLimits.Ceiling.DefaultCpuMillicores);
            clamped.MemoryMb.Should().Be(FunctionLimits.Ceiling.DefaultMemoryMb);
            clamped.TimeoutSeconds.Should().Be(FunctionLimits.Ceiling.DefaultTimeoutSeconds);
            clamped.Concurrency.Should().Be(FunctionLimits.Ceiling.DefaultConcurrency);
        }

        [Fact]
        public void Clamping_normalises_a_zero_rate_limit_to_unlimited()
        {
            // Zero would otherwise mean "refuse everything", which no tenant intends to set.
            var clamped = new FunctionLimits { RequestsPerMinute = 0, RequestsPerDay = 0 }.Clamp();

            clamped.RequestsPerMinute.Should().BeNull();
            clamped.RequestsPerDay.Should().BeNull();
        }

        [Fact]
        public void Concurrency_below_the_floor_is_raised_not_zeroed()
        {
            new FunctionLimits { Concurrency = 1 }.Clamp().Concurrency.Should().Be(1);
        }

        [Fact]
        public void The_ceilings_match_the_runners_own_constants()
        {
            // These two halves live in different repositories and must not drift. If this
            // fails, check plan/DECISIONS.md and the runner's Contracts.Ceilings.
            FunctionLimits.Ceiling.CpuMillicores.Should().Be(200);
            FunctionLimits.Ceiling.MemoryMb.Should().Be(300);
            FunctionLimits.Ceiling.TimeoutSeconds.Should().Be(60);
            FunctionLimits.Ceiling.PidLimit.Should().Be(64);
            FunctionLimits.Ceiling.TmpfsMb.Should().Be(64);
            FunctionLimits.Ceiling.InputBytes.Should().Be(1024 * 1024);
            FunctionLimits.Ceiling.ResultBytes.Should().Be(5L * 1024 * 1024);
            FunctionLimits.Ceiling.LogBytes.Should().Be(1024 * 1024);
            FunctionLimits.Ceiling.LogLines.Should().Be(10_000);
            FunctionLimits.Ceiling.MaxConcurrency.Should().Be(5);
        }

        // ----------------------------------------------------------------- retry ----

        [Fact]
        public void A_single_attempt_never_waits()
        {
            new RetryPolicy { Attempts = 1 }.DelayFor(1).Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void No_backoff_means_no_delay()
        {
            new RetryPolicy { Backoff = BackoffKind.None }.DelayFor(3).Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void Fixed_backoff_is_the_same_every_time()
        {
            var policy = new RetryPolicy { Backoff = BackoffKind.Fixed, InitialDelaySeconds = 7 };

            policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(7));
            policy.DelayFor(5).Should().Be(TimeSpan.FromSeconds(7));
        }

        [Fact]
        public void Exponential_backoff_doubles_and_then_stops_at_the_cap()
        {
            var policy = new RetryPolicy
            {
                Backoff = BackoffKind.Exponential,
                InitialDelaySeconds = 5,
                MaxDelaySeconds = 30,
            };

            policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(5));
            policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(10));
            policy.DelayFor(4).Should().Be(TimeSpan.FromSeconds(20));
            policy.DelayFor(5).Should().Be(TimeSpan.FromSeconds(30), "the cap holds");
            policy.DelayFor(9).Should().Be(TimeSpan.FromSeconds(30));
        }

        // ------------------------------------------------------------ wire mapping ----

        [Theory]
        [InlineData("SUCCEEDED", RunStatus.Succeeded)]
        [InlineData("FAILED", RunStatus.Failed)]
        [InlineData("TIMED_OUT", RunStatus.TimedOut)]
        [InlineData("CANCELLED", RunStatus.Cancelled)]
        [InlineData("RESOURCE_EXCEEDED", RunStatus.ResourceExceeded)]
        [InlineData("OUTPUT_FAILED", RunStatus.OutputFailed)]
        [InlineData("QUEUED", RunStatus.Queued)]
        [InlineData("RUNNING", RunStatus.Running)]
        public void Wire_statuses_round_trip(string wire, RunStatus expected)
        {
            FunctionWireMapping.ToRunStatus(wire, out var recognised).Should().Be(expected);
            recognised.Should().BeTrue();
            FunctionWireMapping.ToWire(expected).Should().Be(wire);
        }

        [Fact]
        public void An_unknown_status_fails_the_run_rather_than_leaving_it_pending()
        {
            // Mapping to Queued would make a finished run look pending forever.
            var status = FunctionWireMapping.ToRunStatus("SOMETHING_NEW", out var recognised);

            recognised.Should().BeFalse();
            status.Should().Be(RunStatus.Failed);
        }

        [Theory]
        [InlineData("MEMORY_LIMIT", RunErrorCode.MemoryLimit)]
        [InlineData("PID_LIMIT", RunErrorCode.PidLimit)]
        [InlineData("USER_RUNTIME_ERROR", RunErrorCode.UserRuntimeError)]
        [InlineData("RESULT_TOO_LARGE", RunErrorCode.ResultTooLarge)]
        [InlineData("TIMED_OUT", RunErrorCode.TimedOut)]
        [InlineData("", RunErrorCode.None)]
        public void Wire_error_codes_map(string wire, RunErrorCode expected)
        {
            FunctionWireMapping.ToErrorCode(wire).Should().Be(expected);
        }

        [Fact]
        public void A_tenants_own_bug_is_not_retried()
        {
            // Re-running code that threw will throw again; only infrastructure failures are
            // worth another attempt.
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.UserRuntimeError)
                .Should().BeFalse();
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.ResultTooLarge)
                .Should().BeFalse();
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.ResultNotSerializable)
                .Should().BeFalse();
        }

        [Fact]
        public void Infrastructure_failures_are_retried()
        {
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.ImagePullFailed)
                .Should().BeTrue();
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.SandboxStartFailed)
                .Should().BeTrue();
            FunctionWireMapping.IsRetryable(RunStatus.ResourceExceeded, RunErrorCode.MemoryLimit)
                .Should().BeTrue();
        }

        [Fact]
        public void A_cancelled_or_successful_run_is_never_retried()
        {
            FunctionWireMapping.IsRetryable(RunStatus.Cancelled, RunErrorCode.None).Should().BeFalse();
            FunctionWireMapping.IsRetryable(RunStatus.Succeeded, RunErrorCode.None).Should().BeFalse();
        }

        [Fact]
        public void Terminal_statuses_are_recognised()
        {
            FunctionWireMapping.IsTerminal(RunStatus.Succeeded).Should().BeTrue();
            FunctionWireMapping.IsTerminal(RunStatus.OutputFailed).Should().BeTrue();
            FunctionWireMapping.IsTerminal(RunStatus.Running).Should().BeFalse();
            FunctionWireMapping.IsTerminal(RunStatus.OutputProcessing).Should().BeFalse();
        }
    }
}
