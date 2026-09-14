using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet.Models;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The build sandbox exists for one reason: <c>docker build</c> cannot be told to use gVisor,
    /// so the one step that runs tenant-chosen code was lifted out of it. These assertions are
    /// what "lifted out" has to mean — anything less and the install is back on the host kernel.
    /// </summary>
    public class BuildSandboxProfileTests
    {
        private const string Work = "/var/lib/blocks-runner/builds/build_1/work";

        private static readonly RunnerOptions Options = new()
        {
            Runtime = Ceilings.SandboxRuntime,
            Network = "blocks-fn-egress",
            ResolvConf = "/etc/blocks-runner/resolv.conf",
            BaseImage = "127.0.0.1:5000/blocks/functions-node:24-v1",
            BuildCpus = 2,
            BuildMemoryMb = 2048,
        };

        private static CreateContainerParameters Create() =>
            BuildSandboxProfile.Create(
                BuildSandboxProfile.ContainerName("build_1"),
                Options.BaseImage,
                Work,
                BuildSandboxProfile.InstallScript("--omit=dev --ignore-scripts", "b", "e"),
                Options);

        [Fact]
        public void Installs_under_gvisor_and_never_the_default_runtime()
        {
            Create().HostConfig.Runtime.Should().Be("runsc");
        }

        [Fact]
        public void Drops_every_privilege_the_way_a_run_does()
        {
            var host = Create().HostConfig;

            host.CapDrop.Should().Contain("ALL");
            host.CapAdd.Should().BeEmpty();
            host.Privileged.Should().BeFalse();
            host.SecurityOpt.Should().Contain("no-new-privileges");
            host.ReadonlyRootfs.Should().BeTrue();
            host.IpcMode.Should().Be("private");
            Create().User.Should().Be($"{Ceilings.SandboxUid}:{Ceilings.SandboxUid}");
        }

        [Fact]
        public void Mounts_only_the_workspace_writable_and_resolv_conf_read_only()
        {
            var binds = Create().HostConfig.Binds;

            binds.Should().HaveCount(2);
            binds.Should().Contain($"{Work}:{BuildSandboxProfile.WorkPath}:rw");
            binds.Should().Contain($"{Options.ResolvConf}:/etc/resolv.conf:ro");
        }

        [Fact]
        public void Runs_on_the_confined_egress_network_with_the_build_budget()
        {
            var host = Create().HostConfig;

            host.NetworkMode.Should().Be("blocks-fn-egress");
            host.NanoCPUs.Should().Be(2_000_000_000);
            host.Memory.Should().Be(2048L * 1024 * 1024);
            // No swap to escape the ceiling into, exactly as for a run.
            host.MemorySwap.Should().Be(host.Memory);
            host.PidsLimit.Should().Be(Ceilings.BuildPidLimit);
        }

        [Fact]
        public void Gives_npm_a_bigger_but_still_noexec_scratch()
        {
            // npm unpacks through /tmp, so 64 MB is not enough — but a package may still not
            // execute what it unpacked there.
            var tmpfs = Create().HostConfig.Tmpfs["/tmp"];

            tmpfs.Should().Contain($"size={Ceilings.BuildTmpfsBytes}");
            tmpfs.Should().Contain("noexec").And.Contain("nosuid").And.Contain("nodev");
        }

        [Fact]
        public void Inherits_nothing_from_the_runner_process()
        {
            // An allowlist, not a filter: the runner holds Redis and Mongo connection strings and
            // none of them may reach a build.
            var env = Create().Env;

            env.Should().OnlyContain(e =>
                e.StartsWith("NODE_ENV=", StringComparison.Ordinal) ||
                e.StartsWith("HOME=", StringComparison.Ordinal) ||
                e.StartsWith("npm_config_", StringComparison.Ordinal) ||
                e.StartsWith("NPM_CONFIG_", StringComparison.Ordinal) ||
                e.StartsWith("NO_COLOR=", StringComparison.Ordinal));
        }

        [Fact]
        public void Overrides_the_bootstrap_entrypoint_with_a_fixed_program()
        {
            // The image's entrypoint is the function bootstrap, which is not what a build wants.
            // What replaces it must not be derived from anything a tenant sent.
            var created = Create();

            created.Entrypoint.Should().Equal("/bin/sh", "-c");
            created.Cmd.Should().ContainSingle();
        }

        // ---- the install script -----------------------------------------------------

        [Fact]
        public void Ignores_lifecycle_scripts_unless_the_function_opted_in()
        {
            BuildSandboxProfile.InstallScript("--omit=dev --ignore-scripts", "b", "e")
                .Should().Contain("--ignore-scripts");

            BuildSandboxProfile.InstallScript("--omit=dev", "b", "e")
                .Should().NotContain("--ignore-scripts");
        }

        [Fact]
        public void Drops_any_lockfile_that_reached_the_workspace_anyway()
        {
            var script = BuildSandboxProfile.InstallScript("--omit=dev", "b", "e");

            foreach (var lockfile in new[]
                     { "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml" })
            {
                script.Should().Contain(lockfile);
            }
        }

        [Fact]
        public void Produces_a_reproducible_archive_so_the_dependency_layer_can_cache()
        {
            var script = BuildSandboxProfile.InstallScript("--omit=dev", "b", "e");

            script.Should().Contain("--sort=name").And.Contain("--mtime=@0").And.Contain("--numeric-owner");
            script.Should().Contain(BuildSandboxProfile.DepsArchiveName);
        }

        [Fact]
        public void Fences_the_resolved_package_list_with_the_build_nonce()
        {
            var script = BuildSandboxProfile.InstallScript("--omit=dev", "BEGIN-nonce", "END-nonce");

            script.Should().Contain("BEGIN-nonce").And.Contain("END-nonce");
            script.Should().Contain("npm ls");
        }

        // ---- validation -------------------------------------------------------------

        private static ContainerInspectResponse Inspect(Action<HostConfig>? weaken = null)
        {
            var created = Create();
            var host = created.HostConfig;
            weaken?.Invoke(host);

            return new ContainerInspectResponse
            {
                HostConfig = host,
                Config = new Config { User = created.User },
            };
        }

        [Fact]
        public void Accepts_a_container_that_matches_the_profile()
        {
            BuildSandboxProfile.Validate(Inspect(), Options).Should().BeNull();
        }

        [Theory]
        [InlineData("runc")]
        [InlineData("")]
        public void Refuses_a_container_the_engine_did_not_place_under_gvisor(string runtime)
        {
            BuildSandboxProfile.Validate(Inspect(h => h.Runtime = runtime), Options)
                .Should().Contain("runsc");
        }

        [Fact]
        public void Refuses_a_writable_resolv_conf()
        {
            var discrepancy = BuildSandboxProfile.Validate(
                Inspect(h => h.Binds = [$"{Work}:{BuildSandboxProfile.WorkPath}:rw", "/etc/x:/etc/resolv.conf:rw"]),
                Options);

            discrepancy.Should().Contain("writable");
        }

        [Fact]
        public void Refuses_a_third_mount()
        {
            var discrepancy = BuildSandboxProfile.Validate(
                Inspect(h => h.Binds =
                [
                    $"{Work}:{BuildSandboxProfile.WorkPath}:rw",
                    "/etc/x:/etc/resolv.conf:ro",
                    "/var/run/docker.sock:/var/run/docker.sock:ro",
                ]),
                Options);

            discrepancy.Should().Contain("exactly two");
        }

        [Fact]
        public void Refuses_a_container_that_kept_its_capabilities()
        {
            BuildSandboxProfile.Validate(Inspect(h => h.CapDrop = []), Options)
                .Should().Contain("capabilities");
        }

        [Fact]
        public void Refuses_a_writable_root_filesystem()
        {
            BuildSandboxProfile.Validate(Inspect(h => h.ReadonlyRootfs = false), Options)
                .Should().Contain("read-only");
        }

        [Fact]
        public void Refuses_a_sandbox_that_could_swap_past_its_ceiling()
        {
            BuildSandboxProfile.Validate(Inspect(h => h.MemorySwap = h.Memory * 2), Options)
                .Should().Contain("memory-swap");
        }

        [Fact]
        public void Does_not_share_a_name_with_a_run_sandbox()
        {
            // The reaper finds run sandboxes by prefix and treats the rest of the name as a run
            // id. A build container that matched that prefix would be looked up as a run, find no
            // lease, and be killed mid-install.
            var build = BuildSandboxProfile.ContainerName("x");

            build.Should().NotStartWith(Blocks.FunctionRunner.Sandbox.SandboxProfile.ContainerPrefix);
            Blocks.FunctionRunner.Sandbox.SandboxProfile.ContainerName("x")
                .Should().NotStartWith(BuildSandboxProfile.ContainerPrefix);
        }
    }
}
