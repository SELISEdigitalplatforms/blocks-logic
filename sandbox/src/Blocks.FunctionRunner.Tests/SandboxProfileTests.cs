using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet.Models;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The profile is the sandbox contract expressed in code. Every assertion here corresponds
    /// to a line in verify/verify.sh that checks the same thing against a real container — one
    /// proves the intent, the other proves the Engine honoured it.
    /// </summary>
    public class SandboxProfileTests
    {
        private static readonly RunnerOptions Options = new()
        {
            Runtime = "runsc",
            Network = "blocks-fn-egress",
            ResolvConf = "/etc/blocks-runner/resolv.conf",
        };

        private static CreateContainerParameters Create(RunLimits? limits = null) =>
            SandboxProfile.Create("blocks-fn-run_1", "img@sha256:abc", "/var/lib/blocks-runner/runs/run_1/execution.json",
                limits ?? RunLimits.Default, Options);

        [Fact]
        public void Runs_under_gvisor_and_never_the_default_runtime()
        {
            Create().HostConfig.Runtime.Should().Be("runsc");
        }

        [Fact]
        public void Configuration_cannot_move_a_sandbox_off_gvisor()
        {
            // RunnerOptions.Runtime is bound from Genesis configuration — runner.env, or the
            // blocks-secret-function-runner document, which lives off this VM. If it were what a
            // sandbox was created with, that document could put every sandbox on the host kernel.
            // It is not: the profile uses the compiled-in constant and the startup guard refuses
            // to claim work when the configured value disagrees.
            var weakened = new RunnerOptions
            {
                Runtime = "runc",
                Network = "blocks-fn-egress",
                ResolvConf = "/etc/blocks-runner/resolv.conf",
            };

            var host = SandboxProfile.Create(
                "blocks-fn-run_1", "img@sha256:abc", "/var/lib/blocks-runner/runs/run_1/execution.json",
                RunLimits.Default, weakened).HostConfig;

            host.Runtime.Should().Be(Ceilings.SandboxRuntime);
        }

        [Fact]
        public void Applies_every_ceiling()
        {
            var host = Create().HostConfig;

            host.NanoCPUs.Should().Be(100_000_000);
            host.Memory.Should().Be(200L * 1024 * 1024);
            host.MemorySwap.Should().Be(200L * 1024 * 1024);
            host.PidsLimit.Should().Be(64);
            host.Tmpfs["/tmp"].Should().Contain("size=67108864").And.Contain("noexec");
        }

        [Fact]
        public void Drops_every_privilege()
        {
            var host = Create().HostConfig;

            host.ReadonlyRootfs.Should().BeTrue();
            host.Privileged.Should().BeFalse();
            host.CapDrop.Should().ContainSingle().Which.Should().Be("ALL");
            host.CapAdd.Should().BeEmpty();
            host.SecurityOpt.Should().Contain("no-new-privileges");
        }

        [Fact]
        public void Runs_as_the_non_root_sandbox_uid()
        {
            Create().User.Should().Be("10001:10001");
        }

        [Fact]
        public void Never_shares_a_host_namespace()
        {
            var host = Create().HostConfig;

            host.PidMode.Should().BeEmpty();
            host.UTSMode.Should().BeEmpty();
            host.UsernsMode.Should().BeEmpty();
            host.IpcMode.Should().Be("private");
        }

        [Fact]
        public void Mounts_only_the_envelope_and_resolv_conf_both_read_only()
        {
            var binds = Create().HostConfig.Binds;

            binds.Should().HaveCount(2);
            binds.Should().OnlyContain(b => b.EndsWith(":ro", StringComparison.Ordinal));
            binds.Should().Contain(b => b.Contains("/run/blocks/execution.json", StringComparison.Ordinal));
            binds.Should().Contain(b => b.Contains("/etc/resolv.conf", StringComparison.Ordinal));
        }

        [Fact]
        public void The_environment_is_an_allowlist_with_no_credentials()
        {
            var env = Create().Env;

            env.Should().HaveCount(4);
            env.Should().Contain("NODE_ENV=production");
            env.Should().Contain("BLOCKS_EXECUTION_FILE=/run/blocks/execution.json");
            env.Should().Contain("NODE_OPTIONS=--max-old-space-size=150");
            env.Should().NotContain(e =>
                e.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("TOKEN", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Never_overrides_the_image_entrypoint()
        {
            // The entrypoint is the trusted bootstrap; tenant code does not choose what runs.
            var parameters = Create();
            parameters.Cmd.Should().BeNull();
            parameters.Entrypoint.Should().BeNull();
        }

        [Fact]
        public void Validation_accepts_a_container_that_matches()
        {
            var inspect = InspectFrom(Create());

            SandboxProfile.Validate(inspect, RunLimits.Default, Options).Should().BeNull();
        }

        [Theory]
        [MemberData(nameof(Weakenings))]
        public void Validation_rejects_a_weakened_container(string label, Action<ContainerInspectResponse> weaken)
        {
            var inspect = InspectFrom(Create());
            weaken(inspect);

            SandboxProfile.Validate(inspect, RunLimits.Default, Options)
                .Should().NotBeNull($"a container with {label} must never be started");
        }

        public static TheoryData<string, Action<ContainerInspectResponse>> Weakenings() => new()
        {
            { "the wrong runtime", i => i.HostConfig.Runtime = "runc" },
            { "no runtime at all", i => i.HostConfig.Runtime = null },
            { "a writable root", i => i.HostConfig.ReadonlyRootfs = false },
            { "privileged mode", i => i.HostConfig.Privileged = true },
            { "added capabilities", i => i.HostConfig.CapAdd = ["SYS_ADMIN"] },
            { "capabilities not dropped", i => i.HostConfig.CapDrop = [] },
            { "no no-new-privileges", i => i.HostConfig.SecurityOpt = [] },
            { "more memory", i => i.HostConfig.Memory = 8L * 1024 * 1024 * 1024 },
            { "more cpu", i => i.HostConfig.NanoCPUs = 4_000_000_000 },
            { "swap enabled", i => i.HostConfig.MemorySwap = -1 },
            { "a raised pid limit", i => i.HostConfig.PidsLimit = 100_000 },
            { "root as the user", i => i.Config.User = "0:0" },
            { "the host network", i => i.HostConfig.NetworkMode = "host" },
            { "an extra mount", i => i.HostConfig.Binds = [.. i.HostConfig.Binds, "/:/host:ro"] },
            { "a writable mount", i => i.HostConfig.Binds = ["/a:/b:rw", "/c:/d:ro"] },
        };

        private static ContainerInspectResponse InspectFrom(CreateContainerParameters p) => new()
        {
            Config = new Config { User = p.User, Env = p.Env },
            HostConfig = p.HostConfig,
        };
    }
}
