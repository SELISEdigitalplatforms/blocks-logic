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
    /// <item>the only mounts are the read-only execution envelope and resolv.conf.</item>
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
        public static CreateContainerParameters Create(
            string containerName,
            string image,
            string envelopeHostPath,
            RunLimits limits,
            RunnerOptions options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(image);
            ArgumentException.ThrowIfNullOrWhiteSpace(envelopeHostPath);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(options);

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
                Env =
                [
                    "NODE_ENV=production",
                    $"BLOCKS_EXECUTION_FILE={EnvelopePath}",
                    "BLOCKS_RUNTIME_VERSION=1",
                    $"NODE_OPTIONS=--max-old-space-size={limits.MaxOldSpaceMb}",
                ],

                Labels = new Dictionary<string, string>
                {
                    [SandboxLabel] = "true",
                },

                AttachStdout = true,
                AttachStderr = true,
                AttachStdin = false,
                OpenStdin = false,
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
                    NanoCPUs = limits.NanoCpus,
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
                    Binds =
                    [
                        $"{envelopeHostPath}:{EnvelopePath}:ro",
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
        /// Re-checks a created container against the profile before it is started. The Engine
        /// is trusted, but a silently ignored field would weaken every sandbox on the host, so
        /// the guarantees are verified rather than assumed.
        /// </summary>
        /// <returns>null when the container matches, otherwise the first discrepancy found.</returns>
        public static string? Validate(ContainerInspectResponse inspect, RunLimits limits, RunnerOptions options)
        {
            ArgumentNullException.ThrowIfNull(inspect);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(options);

            var host = inspect.HostConfig;
            if (host is null) return "the container has no host configuration";

            if (!string.Equals(host.Runtime, Ceilings.SandboxRuntime, StringComparison.Ordinal))
                return $"runtime is '{host.Runtime}', expected '{Ceilings.SandboxRuntime}'";
            if (host.NanoCPUs != limits.NanoCpus)
                return $"NanoCPUs is {host.NanoCPUs}, expected {limits.NanoCpus}";
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
            if (host.Binds is null || host.Binds.Count != 2)
                return $"expected exactly two read-only binds, found {host.Binds?.Count ?? 0}";
            foreach (var bind in host.Binds)
            {
                if (!bind.EndsWith(":ro", StringComparison.Ordinal))
                    return $"bind '{bind}' is not read-only";
            }

            return null;
        }
    }
}
