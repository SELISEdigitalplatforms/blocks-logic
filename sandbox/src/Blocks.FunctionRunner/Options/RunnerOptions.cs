using System.ComponentModel.DataAnnotations;

namespace Blocks.FunctionRunner.Options
{
    /// <summary>
    /// Host-specific runner settings.
    /// <para>
    /// Bound from Genesis configuration: the Mongo <c>Secrets</c> document
    /// <c>blocks-secret-function-runner</c> supplies the shared values, and <c>RUNNER__*</c>
    /// environment variables from <c>/etc/blocks-runner/runner.env</c> override them per host.
    /// Nothing here is a credential — the connection strings live in <c>IBlocksSecret</c>.
    /// </para>
    /// </summary>
    public sealed class RunnerOptions
    {
        public const string SectionName = "Runner";

        /// <summary>Identifies this runner in heartbeats, leases and results. Defaults to the hostname.</summary>
        public string RunnerId { get; set; } = Environment.MachineName;

        /// <summary>
        /// Container runtime for tenant sandboxes, runs and builds alike. Never anything but
        /// <c>runsc</c>: the startup guard compares it with <see cref="Contracts.Ceilings.SandboxRuntime"/>
        /// and refuses to claim work when they differ, so setting it to anything else stops the
        /// host rather than downgrading its isolation. It stays bindable only so a
        /// misconfiguration is reported rather than silently ignored.
        /// </summary>
        [Required]
        public string Runtime { get; set; } = Contracts.Ceilings.SandboxRuntime;

        /// <summary>The confined egress network created by provision/30-network.sh.</summary>
        [Required]
        public string Network { get; set; } = "blocks-fn-egress";

        /// <summary>
        /// resolv.conf bind-mounted over /etc/resolv.conf in every sandbox. Required: Docker's
        /// embedded resolver is unreachable from a gVisor sandbox, so without this DNS fails.
        /// </summary>
        [Required]
        public string ResolvConf { get; set; } = "/etc/blocks-runner/resolv.conf";

        /// <summary>Per-run working directories holding the execution envelope.</summary>
        [Required]
        public string RunsDir { get; set; } = "/var/lib/blocks-runner/runs";

        /// <summary>Per-build workspaces.</summary>
        [Required]
        public string BuildsDir { get; set; } = "/var/lib/blocks-runner/builds";

        /// <summary>
        /// The image store this runner pushes to and pulls from. A loopback address is one registry
        /// per host; anything else is shared with every other runner pointed at it, which changes
        /// what this process may safely delete — see <see cref="PruneRegistry"/>.
        /// </summary>
        public string Registry { get; set; } = "127.0.0.1:5000";

        /// <summary>
        /// Whether the registry admin API is reached over TLS. Null means "decide from
        /// <see cref="Registry"/>": plain HTTP for loopback, HTTPS for anything else, which is the
        /// only combination either is ever deployed as. Set it to override that.
        /// </summary>
        public bool? RegistryTls { get; set; }

        /// <summary>Username for the registry admin API. Empty for an unauthenticated one.</summary>
        public string RegistryUsername { get; set; } = string.Empty;

        /// <summary>Password for <see cref="RegistryUsername"/>. Never logged.</summary>
        public string RegistryPassword { get; set; } = string.Empty;

        /// <summary>
        /// Whether this runner deletes manifests from the registry when it prunes its own image
        /// store. Null means "decide from <see cref="Registry"/>" — true for loopback, false
        /// otherwise — which is the safe reading of both shapes.
        /// <para>
        /// The distinction matters because the "still in use" test is the local container list.
        /// On a per-host registry that is the whole truth. On a shared one it is one host's view:
        /// a run executing on another host, on an image no deployed version pins (a Test build,
        /// say), is invisible here — and deleting its manifest would pull the image out from
        /// under it. So on a shared registry no runner deletes by default; reclaiming blobs there
        /// is one owner's job, either a single runner with this set to true or the registry's own
        /// garbage collection.
        /// </para>
        /// </summary>
        public bool? PruneRegistry { get; set; }

        /// <summary>True when <see cref="Registry"/> names a registry private to this host.</summary>
        public bool IsRegistryHostLocal =>
            Registry.StartsWith("127.", StringComparison.Ordinal)
            || Registry.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase)
            || Registry.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || Registry.StartsWith("[::1]", StringComparison.Ordinal);

        /// <summary>The effective value of <see cref="PruneRegistry"/>.</summary>
        public bool ShouldPruneRegistry => PruneRegistry ?? IsRegistryHostLocal;

        /// <summary>The effective value of <see cref="RegistryTls"/>.</summary>
        public bool UseRegistryTls => RegistryTls ?? !IsRegistryHostLocal;

        /// <summary>Base image tenant images are built FROM, pinned by digest in production.</summary>
        public string BaseImage { get; set; } = "127.0.0.1:5000/blocks/functions-node:24-v1";

        /// <summary>
        /// Pins how many sandboxes run at once. Null — the default — means the number is
        /// discovered from the host instead; see <see cref="Admission.HostBudget"/>. Set it only
        /// to override that on a host where the measurement is known to be wrong, because a
        /// pinned value stops the controller from following the machine in either direction.
        /// <para>Overflow queues either way; nothing is ever rejected for volume.</para>
        /// </summary>
        [Range(1, 1000)]
        public int? MaxActiveSandboxes { get; set; }

        /// <summary>Host memory withheld from admission arithmetic, in MB.</summary>
        [Range(0, 65536)]
        public int ReservedHostMemoryMb { get; set; } = 2048;

        /// <summary>Execution lease TTL. Renewed at a third of this.</summary>
        [Range(1000, 600000)]
        public int LeaseMs { get; set; } = 30_000;

        /// <summary>Heartbeat interval. Must stay below the 15 s heartbeat TTL.</summary>
        [Range(1000, 60000)]
        public int HeartbeatMs { get; set; } = 5_000;

        /// <summary>How long a stream entry may sit idle before another runner may claim it.</summary>
        public int ClaimIdleMs => LeaseMs * 3;

        /// <summary>Delivery attempts before an entry is moved to functions:dead.</summary>
        [Range(1, 20)]
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Grace added to the run timeout before the container is killed outright.</summary>
        [Range(0, 30)]
        public int KillGraceSeconds { get; set; } = 2;

        // ---- builds ------------------------------------------------------------------
        [Range(30, 1800)]
        public int BuildTimeoutSeconds { get; set; } = 300;

        [Range(1, 16)]
        public int BuildCpus { get; set; } = 2;

        [Range(256, 16384)]
        public int BuildMemoryMb { get; set; } = 2048;

        /// <summary>
        /// The host's veto over <c>allowScripts</c>. When set, a build that asks to run npm
        /// lifecycle scripts is refused here, whatever the control plane sent.
        /// <para>
        /// Off by default because the install now happens inside a gVisor sandbox like any other
        /// tenant code, so honouring the opt-in no longer costs a kernel boundary. It exists so an
        /// operator can shut the door on this VM — during an incident, or on a host that should
        /// only ever build inert dependency trees — without waiting on a control-plane change.
        /// </para>
        /// </summary>
        public bool DenyPrivateScriptsOnBuild { get; set; }

        /// <summary>Consume run jobs. Off turns this host into a build-only runner.</summary>
        public bool ProcessRuns { get; set; } = true;

        /// <summary>Consume build jobs.</summary>
        public bool ProcessBuilds { get; set; } = true;
    }
}
