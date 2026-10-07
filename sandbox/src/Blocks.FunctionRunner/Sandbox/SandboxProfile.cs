using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet.Models;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// Builds the container specification for one execution.
    /// <para>
    /// This type is the security profile. Every ceiling and every restriction the platform
    /// promises is expressed here and nowhere else, so the profile can be read in one sitting
    /// and checked against <c>verify/verify.sh</c>, which asserts the same values independently.
    /// </para>
    /// <para>
    /// Deliberate choices worth knowing before editing:
    /// <list type="bullet">
    /// <item>the runtime is always runsc — the runner refuses to start without it, and never
    /// falls back to runc;</item>
    /// <item>memory-swap equals memory, so a sandbox can never swap its way past the ceiling;</item>
    /// <item>the environment is an allowlist, not a filter: nothing is inherited from the
    /// runner process, so no connection string can leak in by accident;</item>
    /// <item>the only writable path is a 64 MB noexec tmpfs at /tmp;</item>
    /// <item>the only mounts are the read-only execution envelope and resolv.conf — and for a
    /// reusable sandbox (<see cref="CreateReusable"/>) resolv.conf alone, its envelopes arriving
    /// on stdin.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class SandboxProfile
    {
        /// <summary>Where the execution envelope is mounted inside the sandbox.</summary>
        public const string EnvelopePath = "/run/blocks/execution.json";

        /// <summary>Label marking a container as a tenant sandbox, so orphans can be found again.</summary>
        public const string SandboxLabel = "dev.selise.blocks.sandbox";

        /// <summary>Prefix of every sandbox container name; the rest is the run id.</summary>
        public const string ContainerPrefix = "blocks-fn-";

        /// <summary>The container name for a run. Deterministic, so a retry finds its own orphan.</summary>
        public static string ContainerName(string runId) => ContainerPrefix + runId;

        /// <summary>
        /// Creates the parameters for a tenant sandbox.
        /// </summary>
        /// <param name="containerName">Name to give the container; must be unique per run.</param>
        /// <param name="image">Image reference, pinned by digest.</param>
        /// <param name="envelopeHostPath">Host path of this run's execution.json.</param>
        /// <param name="limits">Already-clamped limits. Nothing here re-clamps them.</param>
        /// <param name="options">Runner options supplying the network and resolv.conf.</param>
        /// <param name="nanoCpus">The CPU to start with — the start-up boost — or null for the run limit.</param>
        public static CreateContainerParameters Create(
            string containerName,
            string image,
            string envelopeHostPath,
            RunLimits limits,
            RunnerOptions options,
            long? nanoCpus = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(image);
            ArgumentException.ThrowIfNullOrWhiteSpace(envelopeHostPath);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(options);

            return Build(containerName, image, envelopeHostPath, limits, options, nanoCpus);
        }

        // ---- reuse mode (sandbox/REUSE.md) ---------------------------------------------

        /// <summary>Label marking a sandbox as a long-lived, reusable one; the value is the owning runner's id.</summary>
        public const string WarmLabel = "dev.selise.blocks.warm";

        /// <summary>
        /// Prefix of every warm sandbox's name. Deliberately not <see cref="ContainerPrefix"/>
        /// followed by anything: a warm sandbox belongs to no single run, so the lease-based reaper
        /// for run sandboxes must never read its name as a run id.
        /// </summary>
        public const string WarmContainerPrefix = "blocks-fnwarm-";

        /// <summary>
        /// The parameters for a reusable sandbox: the same profile as <see cref="Create"/>, field
        /// for field, with exactly three differences, all forced by serving calls over stdin:
        /// <list type="bullet">
        /// <item>stdin is open and attached, because that is where each call's envelope arrives;</item>
        /// <item>there is no execution.json bind — no envelope exists when the sandbox starts, and
        /// one file per call could not be swapped into a running container anyway;</item>
        /// <item>the environment says <c>BLOCKS_RUNTIME_MODE=reuse</c> and carries the clean grace.</item>
        /// </list>
        /// Same user, same read-only root and tmpfs, same caps, PIDs, network, runtime and limits.
        /// Both modes are built by one private builder precisely so a change to the profile cannot
        /// reach one mode and miss the other.
        /// </summary>
        public static CreateContainerParameters CreateReusable(
            string containerName,
            string image,
            RunLimits limits,
            RunnerOptions options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(image);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(options);

            return Build(containerName, image, envelopeHostPath: null, limits, options, StartNanoCpus(limits, options));
        }

        /// <summary>
        /// The CPU a warm sandbox is created with: the start-up boost when one is configured above
        /// the run limit (<see cref="RunnerOptions.StartBoostMillicores"/>), else the run limit.
        /// Every other limit is the run's from the start.
        /// </summary>
        public static long StartNanoCpus(RunLimits limits, RunnerOptions options)
        {
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(options);
            return options.StartBoostMillicores > limits.CpuMillicores
                ? options.StartBoostMillicores * 1_000_000L
                : limits.NanoCpus;
        }

        private static CreateContainerParameters Build(
            string containerName,
            string image,
            string? envelopeHostPath,
            RunLimits limits,
            RunnerOptions options,
            long? nanoCpus = null)
        {
            var reuse = envelopeHostPath is null;

            List<string> env = reuse
                ?
                [
                    "NODE_ENV=production",
                    "BLOCKS_RUNTIME_MODE=reuse",
                    $"BLOCKS_CLEAN_GRACE_MS={options.CleanGraceMs}",
                    "BLOCKS_RUNTIME_VERSION=1",
                    $"NODE_OPTIONS=--max-old-space-size={limits.MaxOldSpaceMb}",
                ]
                :
                [
                    "NODE_ENV=production",
                    $"BLOCKS_EXECUTION_FILE={EnvelopePath}",
                    "BLOCKS_RUNTIME_VERSION=1",
                    $"NODE_OPTIONS=--max-old-space-size={limits.MaxOldSpaceMb}",
                ];

            var resolvBind = $"{options.ResolvConf}:/etc/resolv.conf:ro";
            List<string> binds = reuse
                ? [resolvBind]
                : [$"{envelopeHostPath}:{EnvelopePath}:ro", resolvBind];

            var labels = new Dictionary<string, string> { [SandboxLabel] = "true" };
            if (reuse) labels[WarmLabel] = options.RunnerId;

            return new CreateContainerParameters
            {
                Name = containerName,
                Image = image,

                // uid/gid 10001. The image declares the same user; setting it here means a
                // rebuilt or substituted image cannot quietly run as root.
                User = $"{Ceilings.SandboxUid}:{Ceilings.SandboxUid}",

                // The image entrypoint is the trusted bootstrap. No command override: tenant
                // code never chooses what the container executes.
                Cmd = null,
                Entrypoint = null,

                // The complete environment. An allowlist, so nothing the runner process holds
                // can reach the sandbox.
                Env = env,

                Labels = labels,

                AttachStdout = true,
                AttachStderr = true,
                AttachStdin = reuse,
                OpenStdin = reuse,
                StdinOnce = false,
                Tty = false,
                NetworkDisabled = false,

                HostConfig = new HostConfig
                {
                    // The compiled-in constant, not options.Runtime. The option is bound from
                    // Genesis configuration and exists so the startup guard can report a host
                    // that was told to use something else; it is deliberately not what a sandbox
                    // is created with, so no configuration path can downgrade this one.
                    Runtime = Ceilings.SandboxRuntime,
                    NetworkMode = options.Network,

                    // --- ceilings -------------------------------------------------------
                    NanoCPUs = nanoCpus ?? limits.NanoCpus,
                    Memory = limits.MemoryBytes,
                    MemorySwap = limits.MemorySwapBytes,
                    MemorySwappiness = 0,
                    PidsLimit = limits.PidLimit,

                    // --- filesystem -----------------------------------------------------
                    ReadonlyRootfs = true,
                    Tmpfs = new Dictionary<string, string>
                    {
                        [ "/tmp" ] = $"rw,noexec,nosuid,nodev,size={limits.TmpfsBytes}",
                    },
                    Binds = binds,

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
        /// Re-checks a created container against the profile before it is started. The Engine
        /// is trusted, but a silently ignored field would weaken every sandbox on the host, so
        /// the guarantees are verified rather than assumed.
        /// </summary>
        /// <returns>null when the container matches, otherwise the first discrepancy found.</returns>
        public static string? Validate(ContainerInspectResponse inspect, RunLimits limits, RunnerOptions options)
            => Validate(inspect, limits, options, reuse: false);

        /// <summary>
        /// <see cref="Validate(ContainerInspectResponse, RunLimits, RunnerOptions)"/> for either
        /// mode. A reusable sandbox has one bind fewer (no execution.json), and must actually be in
        /// reuse mode — otherwise its runtime would look for an execution.json that is not there.
        /// </summary>
        public static string? Validate(
            ContainerInspectResponse inspect, RunLimits limits, RunnerOptions options, bool reuse, long? expectedNanoCpus = null)
        {
            ArgumentNullException.ThrowIfNull(inspect);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(options);

            var host = inspect.HostConfig;
            if (host is null) return "the container has no host configuration";

            // A warm sandbox is checked against the CPU it was created with (the start-up boost),
            // then again against the run limit once it has been dropped (ReusableSandbox).
            var nanoCpus = expectedNanoCpus ?? limits.NanoCpus;
            if (!string.Equals(host.Runtime, Ceilings.SandboxRuntime, StringComparison.Ordinal))
                return $"runtime is '{host.Runtime}', expected '{Ceilings.SandboxRuntime}'";
            if (host.NanoCPUs != nanoCpus)
                return $"NanoCPUs is {host.NanoCPUs}, expected {nanoCpus}";
            if (host.Memory != limits.MemoryBytes)
                return $"memory is {host.Memory}, expected {limits.MemoryBytes}";
            if (host.MemorySwap != limits.MemorySwapBytes)
                return $"memory-swap is {host.MemorySwap}, expected {limits.MemorySwapBytes}";
            if (host.PidsLimit != limits.PidLimit)
                return $"PIDs limit is {host.PidsLimit}, expected {limits.PidLimit}";
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
            var expectedBinds = reuse ? 1 : 2;
            if (host.Binds is null || host.Binds.Count != expectedBinds)
            {
                return reuse
                    ? $"expected exactly one read-only bind, found {host.Binds?.Count ?? 0}"
                    : $"expected exactly two read-only binds, found {host.Binds?.Count ?? 0}";
            }
            foreach (var bind in host.Binds)
            {
                if (!bind.EndsWith(":ro", StringComparison.Ordinal))
                    return $"bind '{bind}' is not read-only";
            }
            if (!string.Equals(host.IpcMode, "private", StringComparison.Ordinal))
                return $"IPC mode is '{host.IpcMode}', expected 'private'";
            if (reuse && inspect.Config?.Env?.Contains("BLOCKS_RUNTIME_MODE=reuse") != true)
                return "the reusable sandbox is not in reuse mode";

            return ValidateTmpfs(host.Tmpfs, limits.TmpfsBytes);
        }

        /// <summary>
        /// The only writable path must be exactly one tmpfs at /tmp that nothing can be executed
        /// from, at the run's size. Checked option by option rather than as a string, and with
        /// the last occurrence of each flag winning, because that is how mount(8) reads them:
        /// <c>noexec,exec</c> is an executable mount.
        /// </summary>
        private static string? ValidateTmpfs(IDictionary<string, string>? tmpfs, long expectedBytes)
        {
            if (tmpfs is null || tmpfs.Count != 1 || !tmpfs.TryGetValue("/tmp", out var raw))
                return $"expected exactly one tmpfs at /tmp, found {(tmpfs is null ? "none" : string.Join(",", tmpfs.Keys))}";

            var options = (raw ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            foreach (var (on, off) in new[] { ("noexec", "exec"), ("nosuid", "suid"), ("nodev", "dev") })
            {
                var last = Array.FindLastIndex(options, o => o == on || o == off);
                if (last < 0 || options[last] != on)
                    return $"the /tmp tmpfs is not {on} ('{raw}')";
            }

            var size = Array.FindLast(options, o => o.StartsWith("size=", StringComparison.Ordinal));
            if (!string.Equals(size, $"size={expectedBytes}", StringComparison.Ordinal))
                return $"the /tmp tmpfs size is '{size}', expected size={expectedBytes}";

            return null;
        }
    }
}
