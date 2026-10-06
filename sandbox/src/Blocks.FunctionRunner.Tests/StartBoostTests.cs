using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A warm sandbox starts with more CPU (Node and the function's imports load ~10× faster) and is
    /// dropped to the run limit at <c>ready</c> — before it can serve anyone. No call ever runs boosted.
    /// </summary>
    public class StartBoostTests
    {
        [Fact]
        public void A_warm_sandbox_is_created_with_the_boost_and_every_other_limit_unchanged()
        {
            var options = new RunnerOptions { StartBoostMillicores = 1000 };
            var create = SandboxProfile.CreateReusable("blocks-fnwarm-x", "img@sha256:abc", RunLimits.Default, options);

            create.HostConfig.NanoCPUs.Should().Be(1_000_000_000L);
            create.HostConfig.Memory.Should().Be(RunLimits.Default.MemoryBytes);
            create.HostConfig.PidsLimit.Should().Be(RunLimits.Default.PidLimit);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        public void Zero_or_a_boost_below_the_limit_means_the_run_limit_from_the_start(int boost)
        {
            var options = new RunnerOptions { StartBoostMillicores = boost };
            SandboxProfile.StartNanoCpus(RunLimits.Default, options).Should().Be(RunLimits.Default.NanoCpus);
        }

        [Fact]
        public void A_single_run_never_gets_the_boost()
        {
            var options = new RunnerOptions { StartBoostMillicores = 1000, RunsDir = "/tmp/x" };
            var create = SandboxProfile.Create("blocks-fn-run_1", "img@sha256:abc", "/tmp/x/run_1/execution.json", RunLimits.Default, options);
            create.HostConfig.NanoCPUs.Should().Be(RunLimits.Default.NanoCpus);
        }

        [Fact]
        public void The_profile_check_takes_the_cpu_it_is_told_to_expect()
        {
            var options = new RunnerOptions { StartBoostMillicores = 1000 };
            var create = SandboxProfile.CreateReusable("blocks-fnwarm-x", "img@sha256:abc", RunLimits.Default, options);
            var inspect = new ContainerInspectResponse
            {
                HostConfig = create.HostConfig,
                Config = new Config { User = create.User, Env = create.Env },
            };

            SandboxProfile.Validate(inspect, RunLimits.Default, options, reuse: true,
                SandboxProfile.StartNanoCpus(RunLimits.Default, options)).Should().BeNull("boosted, as created");
            SandboxProfile.Validate(inspect, RunLimits.Default, options, reuse: true)
                .Should().Contain("NanoCPUs", "still boosted is not the run limit");
        }

        [Fact]
        public async Task The_cpu_is_dropped_at_ready_before_the_sandbox_can_serve_a_call()
        {
            var container = new ScriptedContainer();
            await using var sandbox = new ReusableSandbox(container, new RunnerOptions(), NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);

            start.Status.Should().Be(WarmStartStatus.Ready);
            container.Drops.Should().Be(1);
        }

        [Fact]
        public async Task A_sandbox_whose_drop_cannot_be_confirmed_is_destroyed_not_used()
        {
            var container = new ScriptedContainer { DropFailure = "the start-up CPU could not be dropped: engine said no" };
            await using var sandbox = new ReusableSandbox(container, new RunnerOptions(), NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);

            start.Status.Should().Be(WarmStartStatus.HostFailure);
            start.HostFailure.Should().Contain("could not be dropped");
            container.Killed.Should().BeTrue();
        }
    }
}
