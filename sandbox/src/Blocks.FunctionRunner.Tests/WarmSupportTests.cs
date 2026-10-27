using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Everything around the pool: the drain consumer, the reaper's warm-orphan rule, the
    /// reusable sandbox's security profile, and the stdin framing of the envelope.
    /// </summary>
    public sealed class WarmSupportTests
    {
        private static readonly RunnerOptions Options = new()
        {
            RunnerId = "runner-a",
            Network = "blocks-fn-egress",
            ResolvConf = "/etc/blocks-runner/resolv.conf",
        };

        private static (WarmPool Pool, ScriptedFactory Factory) Pool()
        {
            var options = Microsoft.Extensions.Options.Options.Create(Options);
            var budget = new HostRoom(options, new CalmHost(), NullLogger<HostRoom>.Instance);
            var factory = new ScriptedFactory(Options);
            return (new WarmPool(factory, budget, options, NullLogger<WarmPool>.Instance), factory);
        }

        private static WarmConsumerService Consumer(WarmPool pool) =>
            new(null!, pool, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<WarmConsumerService>.Instance);

        private static ClaimedEntry Entry(params (string Key, string Value)[] fields) =>
            new(RedisValue.Null, fields.ToDictionary(f => f.Key, f => f.Value));

        // ---- drain only, never a pre-warm ------------------------------------------------

        [Fact]
        public async Task An_entry_asking_for_a_prewarm_starts_nothing()
        {
            // An older control plane still sends count and image (RUNNER-FLOW step 2: no pre-warm).
            var (pool, factory) = Pool();
            await using var _ = pool;

            await Consumer(pool).HandleAsync(Entry(
                ("tenantId", "t1"), ("functionId", "fn"), ("versionId", "v2"), ("image", "img:tag"), ("count", "2")));

            factory.Created.Should().BeEmpty("the first call warms a version, not the deploy");
        }

        [Fact]
        public async Task A_drain_entry_drains_the_previous_version_of_that_function_only()
        {
            var (pool, _) = Pool();
            await using var __ = pool;
            await pool.WarmUpAsync(new WarmKey("t1", "fn", "v1", "img1"), 2);
            await pool.WarmUpAsync(new WarmKey("t2", "fn", "v1", "img1"));

            await Consumer(pool).HandleAsync(Entry(
                ("tenantId", "t1"), ("functionId", "fn"), ("versionId", "v2"), ("image", "img2"), ("drainVersionId", "v1")));

            pool.Counts.Total.Should().Be(1, "tenant t2's sandbox is another tenant's");
        }

        // ---- reaper --------------------------------------------------------------------

        [Fact]
        public async Task The_reaper_takes_our_warm_sandboxes_the_pool_no_longer_keeps()
        {
            var (pool, _) = Pool();
            await using var __ = pool;
            var kept = (await pool.AcquireAsync(new WarmKey("t", "f", "v", "i"), RunLimits.Default, default)).Handle!;
            var now = DateTime.UtcNow;

            SandboxReaper.IsOrphanedWarm(kept.Sandbox.Name, "runner-a", now, now, Options, pool).Should().BeFalse();
            SandboxReaper.IsOrphanedWarm("blocks-fnwarm-left-by-a-restart", "runner-a", now, now, Options, pool).Should().BeTrue();
            SandboxReaper.IsOrphanedWarm("blocks-fnwarm-x", "runner-a", now, now, Options, null)
                .Should().BeTrue("after a restart the pool is empty and every one of ours is an orphan");
        }

        [Fact]
        public void The_reaper_leaves_another_runners_warm_sandbox_until_it_is_too_old_to_be_kept()
        {
            var now = DateTime.UtcNow;

            SandboxReaper.IsOrphanedWarm("blocks-fnwarm-x", "runner-b", now.AddMinutes(-30), now, Options, null).Should().BeFalse();
            SandboxReaper.IsOrphanedWarm("blocks-fnwarm-x", "runner-b", now.AddHours(-2), now, Options, null).Should().BeTrue();
        }

        [Fact]
        public void The_reaper_never_reads_a_run_sandbox_as_warm()
        {
            var now = DateTime.UtcNow;
            SandboxReaper.IsOrphanedWarm("blocks-fn-run_1", "runner-a", now, now, Options, null).Should().BeFalse();
        }

        // ---- profile -------------------------------------------------------------------

        [Fact]
        public void A_reusable_sandbox_has_the_run_profile_plus_stdin_and_reuse_mode_and_no_envelope_bind()
        {
            var limits = RunLimits.Default;
            var single = SandboxProfile.Create("blocks-fn-r", "img", "/runs/r/execution.json", limits, Options);
            var warm = SandboxProfile.CreateReusable("blocks-fnwarm-x", "img", limits, Options);

            warm.User.Should().Be(single.User);
            warm.HostConfig.Runtime.Should().Be(Ceilings.SandboxRuntime);
            warm.HostConfig.ReadonlyRootfs.Should().BeTrue();
            warm.HostConfig.Tmpfs.Should().BeEquivalentTo(single.HostConfig.Tmpfs);
            warm.HostConfig.CapDrop.Should().Equal("ALL");
            warm.HostConfig.SecurityOpt.Should().Equal(single.HostConfig.SecurityOpt);
            warm.HostConfig.PidsLimit.Should().Be(single.HostConfig.PidsLimit);
            // Created with the start-up boost; dropped to the single run's CPU at `ready`, before any
            // call (StartBoostTests).
            warm.HostConfig.NanoCPUs.Should().Be(SandboxProfile.StartNanoCpus(limits, Options));
            warm.HostConfig.Memory.Should().Be(single.HostConfig.Memory);
            warm.HostConfig.NetworkMode.Should().Be(single.HostConfig.NetworkMode);
            warm.HostConfig.Binds.Should().ContainSingle().Which.Should().EndWith("/etc/resolv.conf:ro");

            warm.OpenStdin.Should().BeTrue();
            warm.AttachStdin.Should().BeTrue();
            warm.StdinOnce.Should().BeFalse();
            warm.Env.Should().Contain("BLOCKS_RUNTIME_MODE=reuse").And.Contain($"BLOCKS_CLEAN_GRACE_MS={Options.CleanGraceMs}");
            warm.Env.Should().NotContain(e => e.StartsWith("BLOCKS_EXECUTION_FILE=", StringComparison.Ordinal));
            warm.Labels.Should().Contain(SandboxProfile.WarmLabel, "runner-a").And.Contain(SandboxProfile.SandboxLabel, "true");

            single.OpenStdin.Should().BeFalse("the single-run profile is unchanged");
            single.Labels.Should().NotContainKey(SandboxProfile.WarmLabel);
        }

        [Fact]
        public void Validation_of_a_reusable_sandbox_expects_one_bind_and_reuse_mode()
        {
            var limits = RunLimits.Default;
            var warm = SandboxProfile.CreateReusable("blocks-fnwarm-x", "img", limits, Options);
            var inspect = new Docker.DotNet.Models.ContainerInspectResponse
            {
                HostConfig = warm.HostConfig,
                Config = new Docker.DotNet.Models.Config { User = warm.User, Env = warm.Env },
            };

            var boosted = SandboxProfile.StartNanoCpus(limits, Options);
            SandboxProfile.Validate(inspect, limits, Options, reuse: true, boosted).Should().BeNull();
            SandboxProfile.Validate(inspect, limits, Options, reuse: false, boosted).Should().Contain("two read-only binds");

            inspect.Config.Env = [.. warm.Env.Where(e => e != "BLOCKS_RUNTIME_MODE=reuse")];
            SandboxProfile.Validate(inspect, limits, Options, reuse: true, boosted).Should().Contain("reuse mode");
        }

        // ---- envelope framing ------------------------------------------------------------

        [Fact]
        public void The_stdin_envelope_is_one_line_with_every_value_intact()
        {
            var pretty = "{\n  \"run\": { \"id\": \"r1\" },\n  \"input\": { \"text\": \"line1\\nline2 é\", \"n\": 1.50 },\n  \"env\": {}\n}";

            var line = ExecutionEnvelope.ToLine(pretty);

            line.Should().NotContain("\n");
            line.Should().Be("{\"run\":{\"id\":\"r1\"},\"input\":{\"text\":\"line1\\nline2 é\",\"n\":1.50},\"env\":{}}");
        }

        [Fact]
        public void The_stdin_envelope_is_screened_like_the_file()
        {
            var act = () => ExecutionEnvelope.ToLine("""{"run":{"id":"r"},"blocks":{"accessToken":"x"}}""");
            act.Should().Throw<ExecutionEnvelope.ForbiddenContentException>();
        }
    }
}
