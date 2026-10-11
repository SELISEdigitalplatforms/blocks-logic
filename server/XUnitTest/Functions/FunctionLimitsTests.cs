using FluentAssertions;
using RunnerCeilings = Blocks.FunctionRunner.Contracts.Ceilings;
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
        public void A_new_function_starts_on_the_platform_profile()
        {
            var limits = new FunctionLimits();

            limits.CpuMillicores.Should().Be(100);
            limits.MemoryMb.Should().Be(128);
            limits.TimeoutSeconds.Should().Be(30);
            limits.Concurrency.Should().Be(10);
        }

        [Fact]
        public void Rate_limits_are_unlimited()
        {
            var limits = new FunctionLimits();

            limits.RequestsPerMinute.Should().BeNull();
            limits.RequestsPerDay.Should().BeNull();
        }

        /// <summary>
        /// The point of the whole design: what a caller asks for does not matter.
        /// <para>
        /// A greedy request and a modest one produce the same profile, so a tenant can neither widen
        /// a sandbox nor narrow one. Capacity planning then means something — a slot is the same
        /// size whoever is running in it.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(64_000, 65_536, 86_400, 500)]
        [InlineData(150, 156, 5, 3)]
        [InlineData(0, 0, 0, 0)]
        [InlineData(-1, -1, -1, -1)]
        public void Whatever_is_asked_for_the_profile_is_the_same(
            int cpu, int memoryMb, int timeoutSeconds, int concurrency)
        {
            var clamped = new FunctionLimits
            {
                CpuMillicores = cpu,
                MemoryMb = memoryMb,
                TimeoutSeconds = timeoutSeconds,
                Concurrency = concurrency,
            }.Clamp();

            clamped.CpuMillicores.Should().Be(FunctionLimits.Ceiling.CpuMillicores);
            clamped.MemoryMb.Should().Be(FunctionLimits.Ceiling.MemoryMb);
            clamped.TimeoutSeconds.Should().Be(FunctionLimits.Ceiling.TimeoutSeconds);
            clamped.Concurrency.Should().Be(FunctionLimits.Ceiling.Concurrency);
        }

        /// <summary>
        /// A document stored while these were editable keeps loading, and stops mattering.
        /// <para>
        /// Nothing migrates it: every consumer calls <see cref="FunctionLimits.Clamp"/> before a run,
        /// so the old numbers are inert from the moment this ships, and the next save rewrites them.
        /// </para>
        /// </summary>
        [Fact]
        public void An_older_document_with_the_old_ceilings_runs_on_the_new_profile()
        {
            var stored = new FunctionLimits
            {
                MemoryMb = 200,
                TimeoutSeconds = 90,
                Concurrency = 25,
                RequestsPerMinute = 600,
            };

            var effective = stored.Clamp();

            effective.MemoryMb.Should().Be(128);
            effective.TimeoutSeconds.Should().Be(30);
            effective.Concurrency.Should().Be(10);
            // The one value a tenant chooses (FN-19): its HTTP calls per minute, kept when sane.
            effective.RequestsPerMinute.Should().Be(600);
        }

        [Fact]
        public void The_fixed_retry_policy_is_one_retry_five_seconds_later()
        {
            var policy = RetryPolicy.Fixed;

            policy.Attempts.Should().Be(2);
            policy.DelayFor(1).Should().Be(TimeSpan.Zero, "the first attempt never waits");
            policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// The two halves keep separate copies of these constants — the control plane does not
        /// reference the runner's contracts — so this test is the only thing stopping them drifting.
        /// The runner applies its own copy before creating a sandbox, so if they ever disagree the
        /// runner wins and the difference is lost silently to a killed sandbox.
        /// </summary>
        [Fact]
        public void The_profile_matches_the_runners_own_constants()
        {
            FunctionLimits.Ceiling.CpuMillicores.Should().Be(RunnerCeilings.CpuMillicores);
            FunctionLimits.Ceiling.TimeoutSeconds.Should().Be(RunnerCeilings.TimeoutSeconds);
            FunctionLimits.Ceiling.PidLimit.Should().Be(RunnerCeilings.PidLimit);
            FunctionLimits.Ceiling.InputBytes.Should().Be(RunnerCeilings.InputBytes);
            FunctionLimits.Ceiling.ResultBytes.Should().Be(RunnerCeilings.ResultBytes);
            FunctionLimits.Ceiling.LogBytes.Should().Be(RunnerCeilings.LogBytes);
            FunctionLimits.Ceiling.LogLines.Should().Be(RunnerCeilings.LogLines);
            FunctionLimits.Ceiling.Concurrency.Should().Be(RunnerCeilings.MaxFunctionConcurrency);

            // Stated in different units on each side, so converted rather than compared.
            (FunctionLimits.Ceiling.MemoryMb * 1024L * 1024L).Should().Be(RunnerCeilings.MemoryBytes);
            (FunctionLimits.Ceiling.TmpfsMb * 1024L * 1024L).Should().Be(RunnerCeilings.TmpfsBytes);

            // Pinned as literals too, so changing a platform promise takes a deliberate edit here
            // and not just a number somewhere else.
            FunctionLimits.Ceiling.CpuMillicores.Should().Be(100);
            FunctionLimits.Ceiling.MemoryMb.Should().Be(128);
            FunctionLimits.Ceiling.TimeoutSeconds.Should().Be(30);
            FunctionLimits.Ceiling.Concurrency.Should().Be(10);
            FunctionLimits.Ceiling.Attempts.Should().Be(2);
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
        [InlineData("SECRET_UNRESOLVED", RunErrorCode.SecretUnresolved)]
        [InlineData("SECRET_STORE_UNAVAILABLE", RunErrorCode.SecretStoreUnavailable)]
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
        public void A_broken_secret_reference_is_not_retried_but_a_secret_store_outage_is()
        {
            // The same reference to a deleted secret fails the same way every time; a Key Vault
            // blip is gone by the next attempt, and nothing ran either way.
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.SecretUnresolved)
                .Should().BeFalse();
            FunctionWireMapping.IsRetryable(RunStatus.Failed, RunErrorCode.SecretStoreUnavailable)
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
