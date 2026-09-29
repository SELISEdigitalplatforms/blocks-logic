using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet.Models;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// Builds the container specification for one dependency install.
    /// <para>
    /// This exists because <c>docker build</c> cannot be told which runtime to use: a RUN step
    /// always executes on the Engine's default runtime, which is deliberately <c>runc</c>. For
    /// every other step that is fine — they run commands this repository wrote. <c>npm install</c>
    /// is the exception: it fetches and, when a function opts in, <i>executes</i> code the tenant
    /// chose. Running that on the host kernel was the one place in the pipeline where tenant code
    /// escaped gVisor, so the install was lifted out of the image build and into a sandbox of its
    /// own, which this type describes.
    /// </para>
    /// <para>
    /// It is deliberately a near-copy of <see cref="Sandbox.SandboxProfile"/> rather than a shared
    /// abstraction over it. The two profiles must be readable side by side and must be allowed to
    /// diverge exactly where a build genuinely differs from a run; folding them together would
    /// make a change to one silently reshape the other, which is the last thing either should do.
    /// The differences, all of them, are:
    /// <list type="bullet">
    /// <item>a writable bind mount, because an install has to produce something;</item>
    /// <item>a larger PID ceiling and /tmp, because npm forks per package and unpacks through
    /// /tmp;</item>
    /// <item>the build's CPU and memory budget rather than a run's;</item>
    /// <item>an entrypoint override, because the image's own entrypoint is the function
    /// bootstrap.</item>
    /// </list>
    /// Everything else — gVisor, cap-drop, no-new-privileges, non-root uid, read-only root, the
    /// confined egress network, the environment allowlist — is identical, and is meant to stay
    /// identical.
    /// </para>
    /// </summary>
    public static class BuildSandboxProfile
    {
        /// <summary>Where the install workspace is mounted inside the sandbox.</summary>
        public const string WorkPath = "/work";

        /// <summary>The tarball the install produces, relative to <see cref="WorkPath"/>.</summary>
        public const string DepsArchiveName = "deps.tar";

        /// <summary>Label marking a container as a build sandbox, so orphans can be found again.</summary>
        public const string BuildSandboxLabel = "dev.selise.blocks.buildsandbox";

        /// <summary>
        /// Prefix of every build sandbox's name. Deliberately not a prefix of
        /// <see cref="Sandbox.SandboxProfile.ContainerPrefix"/> and not prefixed by it either, so
        /// neither the reaper nor an operator's <c>docker ps</c> filter can confuse the two.
        /// </summary>
        public const string ContainerPrefix = "blocks-fnbuild-";

        /// <summary>The container name for a build. Deterministic, so a retry finds its own orphan.</summary>
        public static string ContainerName(string buildId) => ContainerPrefix + buildId;

        /// <summary>
        /// The shell program the sandbox runs.
        /// <para>
        /// Every value interpolated here is one this repository produced — the npm flags are
        /// chosen from two fixed strings and the markers are a runner-generated nonce. Tenant
        /// input reaches this container as <i>files</i> in the mounted workspace, never as text in
        /// this command, which is what keeps a package name from becoming shell.
        /// </para>
        /// <para>
        /// The tar is written with a fixed sort order, zeroed timestamps and numeric root
        /// ownership so that an unchanged dependency tree produces a byte-identical archive. That
        /// is what lets the image build below it hit its layer cache instead of rebuilding a
        /// dependency layer on every build.
        /// </para>
        /// </summary>
        public static string InstallScript(string npmFlags, string beginMarker, string endMarker)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(npmFlags);
            ArgumentException.ThrowIfNullOrWhiteSpace(beginMarker);
            ArgumentException.ThrowIfNullOrWhiteSpace(endMarker);

            return $"""
                set -eu
                cd {WorkPath}
                rm -f package-lock.json npm-shrinkwrap.json yarn.lock pnpm-lock.yaml
                npm install {npmFlags} --no-audit --no-fund
                echo "{beginMarker}"
                npm ls --omit=dev --depth=0 --json 2>/dev/null || true
                echo "{endMarker}"
                tar --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner \
                    -cf {WorkPath}/{DepsArchiveName} node_modules
                """;
        }

        /// <summary>
        /// Creates the parameters for one dependency install.
        /// </summary>
        /// <param name="containerName">Name to give the container; must be unique per build.</param>
        /// <param name="image">The base image, pinned by digest in production.</param>
        /// <param name="workHostPath">Host directory bind-mounted read-write at <see cref="WorkPath"/>.</param>
        /// <param name="script">The program from <see cref="InstallScript"/>.</param>
        /// <param name="options">Runner options supplying the runtime, network and budgets.</param>
        public static CreateContainerParameters Create(
            string containerName,
            string image,
            string workHostPath,
            string script,
            RunnerOptions options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(image);
            ArgumentException.ThrowIfNullOrWhiteSpace(workHostPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(script);
            ArgumentNullException.ThrowIfNull(options);

            return new CreateContainerParameters
            {
                Name = containerName,
                Image = image,

                // The same non-root identity a function runs as. The workspace on the host is
                // opened up to it rather than the sandbox being given the runner's identity:
                // nothing in a build has any business running as the service account.
                User = $"{Ceilings.SandboxUid}:{Ceilings.SandboxUid}",

                // The image entrypoint is the function bootstrap, which is not what a build
                // wants. This is the one place an entrypoint is overridden, and it is overridden
                // with a fixed program, never with anything derived from tenant input.
                Entrypoint = ["/bin/sh", "-c"],
                Cmd = [script],

                // The complete environment. An allowlist, as for a run. HOME and the npm cache
                // are pointed into the workspace because the root filesystem is read-only and npm
                // writes to both.
                Env =
                [
                    "NODE_ENV=production",
                    $"HOME={WorkPath}",
                    $"npm_config_cache={WorkPath}/.npm-cache",
                    "NPM_CONFIG_UPDATE_NOTIFIER=false",
                    // npm writes progress with escape codes when it thinks it has a terminal; the
                    // build log is read by people and parsed for the resolved-package block.
                    "NO_COLOR=1",
                    "npm_config_progress=false",
                ],

                Labels = new Dictionary<string, string>
                {
                    [BuildSandboxLabel] = "true",
                },

                AttachStdout = true,
                AttachStderr = true,
                AttachStdin = false,
                OpenStdin = false,
                Tty = false,
                NetworkDisabled = false,
                WorkingDir = WorkPath,

                HostConfig = new HostConfig
                {
                    // The compiled-in constant, never options.Runtime — see SandboxProfile.
                    Runtime = Ceilings.SandboxRuntime,
                    NetworkMode = options.Network,

                    // --- budgets --------------------------------------------------------
                    NanoCPUs = options.BuildCpus * 1_000_000_000L,
                    Memory = (long)options.BuildMemoryMb * 1024 * 1024,
                    MemorySwap = (long)options.BuildMemoryMb * 1024 * 1024,
                    MemorySwappiness = 0,
                    PidsLimit = Ceilings.BuildPidLimit,

                    // --- filesystem -----------------------------------------------------
                    // Read-only root, as for a run. The workspace is the single writable path
                    // and it is a bind mount, so what the install produces survives the
                    // container and can be read back by the runner.
                    ReadonlyRootfs = true,
                    Tmpfs = new Dictionary<string, string>
                    {
                        // noexec is the difference that matters: a package may unpack whatever it
                        // likes through /tmp, but it may not execute it from there.
                        [ "/tmp" ] = $"rw,noexec,nosuid,nodev,size={Ceilings.BuildTmpfsBytes}",
                    },
                    Binds =
                    [
                        $"{workHostPath}:{WorkPath}:rw",
                        $"{options.ResolvConf}:/etc/resolv.conf:ro",
                    ],

                    // --- privileges -----------------------------------------------------
                    CapDrop = ["ALL"],
                    CapAdd = [],
                    Privileged = false,
                    SecurityOpt = ["no-new-privileges"],

                    // --- namespaces: never shared with the host -------------------------
                    PidMode = string.Empty,
                    IpcMode = "private",
                    UTSMode = string.Empty,
                    UsernsMode = string.Empty,

                    // --- lifecycle ------------------------------------------------------
                    AutoRemove = false,   // the runner inspects the corpse before removing it
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
                    Init = false,
                    OomKillDisable = false,

                    LogConfig = new LogConfig
                    {
                        Type = "json-file",
                        Config = new Dictionary<string, string> { ["max-size"] = "5m", ["max-file"] = "1" },
                    },
                },
            };
        }

        /// <summary>
        /// Re-checks a created build sandbox against the profile before it is started, for the
        /// same reason <see cref="Sandbox.SandboxProfile.Validate"/> does: a field the Engine
        /// silently ignored would weaken the boundary without anything saying so.
        /// </summary>
        /// <returns>null when the container matches, otherwise the first discrepancy found.</returns>
        public static string? Validate(ContainerInspectResponse inspect, RunnerOptions options)
        {
            ArgumentNullException.ThrowIfNull(inspect);
            ArgumentNullException.ThrowIfNull(options);

            var host = inspect.HostConfig;
            if (host is null) return "the container has no host configuration";

            // The check that the whole change exists for.
            if (!string.Equals(host.Runtime, Ceilings.SandboxRuntime, StringComparison.Ordinal))
                return $"runtime is '{host.Runtime}', expected '{Ceilings.SandboxRuntime}'";
            if (!host.ReadonlyRootfs)
                return "the root filesystem is not read-only";
            if (host.Privileged)
                return "the container is privileged";
            if (host.CapAdd is { Count: > 0 })
                return $"capabilities were added: {string.Join(",", host.CapAdd)}";
            if (host.CapDrop is null || !host.CapDrop.Contains("ALL"))
                return "capabilities were not all dropped";
            if (host.SecurityOpt is null || !host.SecurityOpt.Contains("no-new-privileges"))
                return "no-new-privileges is not set";
            if (!string.Equals(inspect.Config?.User, $"{Ceilings.SandboxUid}:{Ceilings.SandboxUid}", StringComparison.Ordinal))
                return $"user is '{inspect.Config?.User}', expected {Ceilings.SandboxUid}:{Ceilings.SandboxUid}";
            if (!string.Equals(host.NetworkMode, options.Network, StringComparison.Ordinal))
                return $"network is '{host.NetworkMode}', expected '{options.Network}'";
            if (host.Memory != (long)options.BuildMemoryMb * 1024 * 1024)
                return $"memory is {host.Memory}, expected {(long)options.BuildMemoryMb * 1024 * 1024}";
            if (host.MemorySwap != host.Memory)
                return $"memory-swap is {host.MemorySwap}, expected it to equal memory ({host.Memory})";
            if (host.PidsLimit != Ceilings.BuildPidLimit)
                return $"PIDs limit is {host.PidsLimit}, expected {Ceilings.BuildPidLimit}";

            // Exactly two mounts, and only the workspace is writable. A third bind, or a
            // writable resolv.conf, would mean something reached the sandbox that this profile
            // never granted.
            if (host.Binds is null || host.Binds.Count != 2)
                return $"expected exactly two binds, found {host.Binds?.Count ?? 0}";
            foreach (var bind in host.Binds)
            {
                var writable = bind.EndsWith(":rw", StringComparison.Ordinal);
                var isWorkspace = bind.Contains($":{WorkPath}:", StringComparison.Ordinal);
                if (writable != isWorkspace)
                {
                    return isWorkspace
                        ? $"the workspace bind '{bind}' is not writable"
                        : $"bind '{bind}' is writable and only the workspace may be";
                }
            }

            return null;
        }
    }
}
