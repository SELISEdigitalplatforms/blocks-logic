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

        /// <summary>Local image store.</summary>
        public string Registry { get; set; } = "127.0.0.1:5000";

        /// <summary>Base image tenant images are built FROM, pinned by digest in production.</summary>
        public string BaseImage { get; set; } = "127.0.0.1:5000/blocks/functions-node:24-v1";

        /// <summary>Sandboxes admitted at once. Overflow queues; nothing is rejected.</summary>
        [Range(1, 100)]
        public int MaxActiveSandboxes { get; set; } = Contracts.Ceilings.DefaultHostAdmission;

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
