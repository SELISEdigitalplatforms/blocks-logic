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
        public void A_single_runs_profile_starts_at_the_run_limit_unless_told_the_boost()
        {
            var options = new RunnerOptions { StartBoostMillicores = 1000, RunsDir = "/tmp/x" };
            var plain = SandboxProfile.Create("blocks-fn-run_1", "img@sha256:abc", "/tmp/x/run_1/execution.json", RunLimits.Default, options);
            plain.HostConfig.NanoCPUs.Should().Be(RunLimits.Default.NanoCpus);

            var boosted = SandboxProfile.Create("blocks-fn-run_1", "img@sha256:abc", "/tmp/x/run_1/execution.json", RunLimits.Default, options,
                SandboxProfile.StartNanoCpus(RunLimits.Default, options));
            boosted.HostConfig.NanoCPUs.Should().Be(1_000_000_000L);
            boosted.HostConfig.Memory.Should().Be(RunLimits.Default.MemoryBytes, "only the CPU differs");
        }

        // ---- FN-6: the boost is capped (2026-10-07) ---------------------------------------------

        private static async Task Eventually(Func<bool> condition)
        {
            for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
            condition().Should().BeTrue();
        }

        [Fact]
        public async Task A_start_not_ready_by_the_cap_is_dropped_by_the_cap_and_not_again_at_ready()
        {
            var container = new ScriptedContainer();
            container.OnStart.Clear();
            await using var sandbox = new ReusableSandbox(container, new RunnerOptions { StartBoostMaxMs = 50 }, NullLogger.Instance);

            var start = sandbox.StartAsync(CancellationToken.None);
            await Eventually(() => container.Drops == 1);
            start.IsCompleted.Should().BeFalse("a slow start goes on, at the run limit");

            container.Push(Lines.Ready());
            (await start).Status.Should().Be(WarmStartStatus.Ready);
            container.Drops.Should().Be(1, "one drop, shared by the cap and ready");
        }

        [Fact]
        public async Task A_start_ready_before_the_cap_is_dropped_once()
        {
            var container = new ScriptedContainer();
            await using var sandbox = new ReusableSandbox(container, new RunnerOptions { StartBoostMaxMs = 50 }, NullLogger.Instance);

            (await sandbox.StartAsync(CancellationToken.None)).Status.Should().Be(WarmStartStatus.Ready);
            await Task.Delay(150);

            container.Drops.Should().Be(1);
        }

        [Fact]
        public async Task A_cap_drop_that_cannot_be_confirmed_kills_the_start_as_the_hosts_failure()
        {
            var container = new ScriptedContainer { DropFailure = "the start-up CPU could not be dropped: engine said no" };
            container.OnStart.Clear();
            await using var sandbox = new ReusableSandbox(container, new RunnerOptions { StartBoostMaxMs = 50 }, NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            start.Status.Should().Be(WarmStartStatus.HostFailure);
            start.HostFailure.Should().Contain("could not be dropped");
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task With_the_cap_off_a_slow_start_keeps_the_boost_until_ready()
        {
            var container = new ScriptedContainer();
            container.OnStart.Clear();
            await using var sandbox = new ReusableSandbox(container, new RunnerOptions { StartBoostMaxMs = 0 }, NullLogger.Instance);

            var start = sandbox.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            container.Drops.Should().Be(0);

            container.Push(Lines.Ready());
            await start;
            container.Drops.Should().Be(1);
        }

        // ---- single runs: dropped at `started` or the cap -----------------------------------

        private sealed class CountingDrop
        {
            public int Calls;
            public string? Failure;
            public Task<string?> Drop() { Interlocked.Increment(ref Calls); return Task.FromResult(Failure); }
        }

        [Fact]
        public async Task A_single_runs_boost_is_dropped_at_its_started_line_even_split_across_reads()
        {
            var drop = new CountingDrop();
            var boost = new DockerSandbox.SingleRunBoost(drop.Drop);

            boost.OnText("{\"t\":\"log\",\"msg\":\"loading\"}\n{\"t\":\"sta");
            drop.Calls.Should().Be(0);
            boost.OnText("rted\",\"at\":1}\n");
            boost.OnText("{\"t\":\"started\",\"at\":2}\n");

            drop.Calls.Should().Be(1);
            (await boost.CloseAsync()).Should().BeNull();
        }

        [Fact]
        public void The_word_started_inside_a_log_message_does_not_drop_it()
        {
            var drop = new CountingDrop();
            var boost = new DockerSandbox.SingleRunBoost(drop.Drop);

            // A log line as protocol.mjs encodes it: the quotes of the message are escaped.
            boost.OnText("""{"t":"log","msg":"{\"t\":\"started\"} and started"}""" + "\n");

            drop.Calls.Should().Be(0);
        }

        [Fact]
        public async Task A_single_runs_cap_drops_it_and_nothing_drops_after_close()
        {
            var drop = new CountingDrop { Failure = "the start-up CPU could not be dropped: no" };
            var boost = new DockerSandbox.SingleRunBoost(drop.Drop);

            await boost.CapAsync(10, CancellationToken.None);
            drop.Calls.Should().Be(1);
            (await boost.CloseAsync()).Should().Contain("could not be dropped", "the run reads the failure");

            var late = new CountingDrop();
            var closed = new DockerSandbox.SingleRunBoost(late.Drop);
            (await closed.CloseAsync()).Should().BeNull();
            await closed.CapAsync(10, CancellationToken.None);
            closed.OnText("""{"t":"started","at":1}""");
            late.Calls.Should().Be(0, "the container is being removed");
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
