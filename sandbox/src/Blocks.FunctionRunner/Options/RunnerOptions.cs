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
        /// Scratch space for downloading and unpacking a run's build artifact before it is built
        /// into a local image. Emptied after each build — the image is what lasts.
        /// </summary>
        [Required]
        public string ArtifactsDir { get; set; } = "/var/lib/blocks-runner/artifacts";

        /// <summary>
        /// How many function images this host keeps before evicting the least recently used, as
        /// <c>RUNNER__MaxCachedImages</c>.
        /// <para>
        /// Zero (the default) derives it from the disk instead, so a 64 GB temp disk and a 512 GB
        /// data disk both do something sensible without anyone setting a number. Set it to pin an
        /// explicit count. Disk is a cache here, not a store: an evicted image rebuilds from its
        /// artifact in seconds, so holding too few costs latency, never correctness.
        /// </para>
        /// </summary>
        [Range(0, 100_000)]
        public int MaxCachedImages { get; set; }

        /// <summary>
        /// The share of the image disk function images may occupy, as <c>RUNNER__ImageDiskPercent</c>.
        /// Whichever of this and <see cref="MaxCachedImages"/> binds first is the one that evicts.
        /// Builds and run directories need room on the same disk, which is why this is well under 100.
        /// </summary>
        [Range(10, 95)]
        public int ImageDiskPercent { get; set; } = 70;

        /// <summary>
        /// The image store this runner pushes to and pulls from. A loopback address is one registry
        /// per host; anything else is shared with every other runner pointed at it, which changes
        /// what this process may safely delete — see <see cref="PruneRegistry"/>.
        /// </summary>
        public string Registry { get; set; } = "127.0.0.1:5000";

        /// <summary>
        /// Redis namespace for the queues this runner serves, as <c>RUNNER__QueuePrefix</c>.
        /// <para>
        /// Empty by default, which is exactly the behaviour this runner has always had. It must
        /// match <c>Functions:QueuePrefix</c> on the control plane it serves — the two halves
        /// name the same streams, and a mismatch is silent: the runner joins streams nobody
        /// writes to and simply never receives work.
        /// </para>
        /// <para>
        /// Set it wherever more than one environment shares a Redis. Unset, every runner on that
        /// Redis joins one consumer group and competes for the same entries, so a test enqueued
        /// by one developer's control plane can be built on another developer's host — against
        /// that host's private registry, and whatever build of this runner it happens to have.
        /// </para>
        /// </summary>
        public string QueuePrefix { get; set; } = string.Empty;

        /// <summary>
        /// The most sandbox slots one tenant may hold at once, as
        /// <c>RUNNER__MaxSandboxesPerTenant</c>. Zero (the default) derives it as half the host's
        /// capacity, rounded up.
        /// <para>
        /// The host budget on its own is first-come-first-served, so one tenant with a burst
        /// could hold every slot on a runner while every other tenant waited behind it. Nothing
        /// is rejected either way — a tenant over its share has its entries deferred, the same as
        /// any other gate here. Set it to the host capacity or higher to turn the share off.
        /// </para>
        /// </summary>
        public int MaxSandboxesPerTenant { get; set; }

        /// <summary>The effective value of <see cref="MaxSandboxesPerTenant"/> for a given capacity.</summary>
        public int TenantSlotLimit(int hostCapacity) =>
            MaxSandboxesPerTenant > 0 ? MaxSandboxesPerTenant : Math.Max(1, (hostCapacity + 1) / 2);

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

        /// <summary>
        /// How many deployed runs this host works on at once. 0 (the default) follows the host
        /// budget's capacity as it moves; admission still decides, per run, whether it may start.
        /// 1 is the old behaviour — one run at a time, the next claimed only when it is over — kept
        /// as a switch to fall back to without a redeploy.
        /// </summary>
        [Range(0, 256)]
        public int MaxParallelRuns { get; set; }

        /// <summary>Delivery attempts before an entry is moved to functions:dead.</summary>
        [Range(1, 20)]
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Grace added to the run timeout before the container is killed outright.</summary>
        [Range(0, 30)]
        public int KillGraceSeconds { get; set; } = 2;

        /// <summary>
        /// Time allowed for a sandbox to reach the tenant's first line of code, as
        /// <c>RUNNER__StartupAllowanceSeconds</c>.
        /// <para>
        /// Booting gVisor, starting Node and importing a dependency tree is the platform's time, not
        /// the function's. It used to have to fit inside <see cref="KillGraceSeconds"/>, which is two
        /// seconds — so a function with a large dependency tree was killed as TIMED_OUT while its
        /// handler was still well inside its own budget, and the failure read as the tenant's fault.
        /// </para>
        /// <para>
        /// The cost of a generous value is that a sandbox wedged <em>before</em> the handler starts
        /// holds its slot for this long. That is the right way round: a slow import is common and a
        /// wedged boot is not.
        /// </para>
        /// </summary>
        [Range(1, 300)]
        public int StartupAllowanceSeconds { get; set; } = 30;

        /// <summary>
        /// How full this host may be before a <em>test</em> run is made to wait, as
        /// <c>RUNNER__TestDeferAbovePercent</c>. 100 turns the rule off.
        /// <para>
        /// A test is a developer convenience; a deployed function is somebody's traffic. When the
        /// host is busy the test can wait a few seconds and nobody minds — and nothing is refused,
        /// it simply stays on the queue, which is how everything else here handles load.
        /// </para>
        /// </summary>
        [Range(10, 100)]
        public int TestDeferAbovePercent { get; set; } = 70;

        /// <summary>
        /// Keep an installed dependency tree so a build whose <c>package.json</c> has not changed
        /// does not install it again, as <c>RUNNER__CacheDependencies</c>.
        /// <para>
        /// On by default. The install is the expensive half of a build, and while a function is
        /// being written its manifest rarely changes — so most builds can skip it entirely. The
        /// trade is that an unchanged manifest stops silently picking up newer patch versions,
        /// which is arguably the behaviour you want anyway.
        /// </para>
        /// </summary>
        public bool CacheDependencies { get; set; } = true;

        /// <summary>
        /// How many dependency trees this host keeps, as <c>RUNNER__MaxCachedDependencyTrees</c>.
        /// <para>
        /// Its own small budget rather than a share of the disk, on purpose: the image cache shares
        /// that disk, and a dependency cache allowed to grow into it would evict the images that
        /// deployed functions run from. Helping tests must not cost production.
        /// </para>
        /// </summary>
        [Range(1, 10_000)]
        public int MaxCachedDependencyTrees { get; set; } = 50;

        // ---- builds ------------------------------------------------------------------
        [Range(30, 1800)]
        public int BuildTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// A ceiling on the CPU a build may use, as <c>RUNNER__BuildCpus</c>.
        /// <para>
        /// Zero (the default) means <b>no ceiling</b>, which is the point: a build is held back by
        /// <see cref="BuildCpuShares"/> instead, so it uses whatever the host has spare and yields
        /// the moment a run wants it. A hard quota does the opposite — it takes its cores whether
        /// production needs them or not, which is what made a developer clicking Test cost the
        /// deployed version its capacity.
        /// </para>
        /// <para>Set a number only to cap a build on a host where even spare-time builds are unwelcome.</para>
        /// </summary>
        [Range(0, 16)]
        public int BuildCpus { get; set; }

        /// <summary>
        /// The build's share of CPU when it is competing, as <c>RUNNER__BuildCpuShares</c>.
        /// <para>
        /// Relative, not absolute: the default for a container is 1024, so 128 means a build gets
        /// roughly an eighth of the attention a run does when both want the CPU — and all of it when
        /// nothing else does. That is the whole idea. A build is never urgent; a run always is.
        /// </para>
        /// </summary>
        [Range(2, 1024)]
        public int BuildCpuShares { get; set; } = 128;

        /// <summary>
        /// The build's share of disk I/O when it is competing, as <c>RUNNER__BuildBlkioWeight</c>.
        /// <para>
        /// The same idea as <see cref="BuildCpuShares"/> and it matters just as much: an install
        /// unpacks thousands of small files, and a host starved of I/O stalls runs that are only
        /// trying to read their own image. 10 is the floor the kernel accepts, 500 the default.
        /// </para>
        /// </summary>
        [Range(10, 1000)]
        public ushort BuildBlkioWeight { get; set; } = 100;

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

        /// <summary>
        /// The image the dependency install runs in, as <c>RUNNER__BuildImage</c>. Empty — the
        /// default — means <see cref="BaseImage"/>, which is what every build used before this
        /// option existed.
        /// <para>
        /// It exists for native add-ons. The run image is deliberately bare: no compiler, no
        /// Python, no make, because nothing at run time should be able to build code. An add-on
        /// without a prebuilt binary for this platform compiles during <c>npm install</c>
        /// (node-gyp), so the install needs that toolchain — and only the install. Point this at a
        /// build image that is the run image plus the toolchain (same Debian release, same Node,
        /// so what compiles here loads there) and the toolchain stays out of every sandbox that
        /// runs tenant code. The install sandbox's profile does not change with it.
        /// </para>
        /// </summary>
        public string BuildImage { get; set; } = string.Empty;

        /// <summary>The effective value of <see cref="BuildImage"/>.</summary>
        public string EffectiveBuildImage => string.IsNullOrWhiteSpace(BuildImage) ? BaseImage : BuildImage;

        // ---- sandbox reuse (sandbox/REUSE.md) -----------------------------------------
        /// <summary>
        /// Lets a run be served by a warm, reused sandbox, as <c>RUNNER__SandboxReuse</c>.
        /// <para>
        /// Off by default, and off means exactly the old path: one fresh sandbox per run. On, a run
        /// still uses the old path unless its own entry carries <c>reuse=1</c> — the function opted
        /// in. Two keys on purpose: the tenant decides whether its code tolerates living on between
        /// calls, and the operator decides whether this host does reuse at all.
        /// </para>
        /// </summary>
        public bool SandboxReuse { get; set; }

        /// <summary>
        /// How long a warm sandbox may sit idle (paused) before it is destroyed, as
        /// <c>RUNNER__WarmIdleSeconds</c>. An idle sandbox costs memory and nothing else — paused,
        /// it gets no CPU — so this is the memory a quiet function is allowed to keep.
        /// </summary>
        [Range(5, 86_400)]
        public int WarmIdleSeconds { get; set; } = 600;

        /// <summary>
        /// Calls one sandbox serves before it is recycled, as <c>RUNNER__WarmMaxCalls</c>. A bound
        /// on whatever slowly accumulates in a long-lived process that "clean" cannot see — a cache
        /// that only grows, a leak in a library.
        /// </summary>
        [Range(1, 1_000_000)]
        public int WarmMaxCalls { get; set; } = 1000;

        /// <summary>
        /// Oldest a warm sandbox may become, busy or not, as <c>RUNNER__WarmMaxAgeSeconds</c>. The
        /// same bound as <see cref="WarmMaxCalls"/> in time rather than calls, and the reason no
        /// tenant process on this host is ever older than this.
        /// </summary>
        [Range(10, 86_400)]
        public int WarmMaxAgeSeconds { get; set; } = 3600;

        /// <summary>
        /// Memory use, as a percentage of the sandbox's own limit, above which a sandbox is not
        /// reused after its call, as <c>RUNNER__WarmMemoryHighWaterPercent</c>. Read from Docker's
        /// cgroup stats, never from the runtime's own rssBytes — under gVisor that figure is the
        /// sentry's view and does not track what the cgroup will kill at.
        /// </summary>
        [Range(10, 100)]
        public int WarmMemoryHighWaterPercent { get; set; } = 90;

        /// <summary>
        /// Most warm sandboxes one function version may have on this host, as
        /// <c>RUNNER__WarmMaxPerVersion</c>. Zero (the default) uses the function's own concurrency
        /// limit, which is the most that could ever be busy at once anyway.
        /// </summary>
        [Range(0, 1000)]
        public int WarmMaxPerVersion { get; set; }

        /// <summary>
        /// CPU the host spends starting one sandbox, outside the sandbox's own quota, as
        /// <c>RUNNER__StartCostCpuMs</c>.
        /// <para>
        /// Docker, containerd and gVisor setting up a container cost ~0.7–1.0 CPU-seconds on this
        /// host (measured, sandbox/REUSE.md) — about eight seconds of a sandbox's whole 0.1-CPU
        /// quota, paid by nobody's cgroup. Admission charges it on every start, single or warm,
        /// and plans its initial slot count around it. Zero turns the charge, and the start rate
        /// limit derived from it, off.
        /// </para>
        /// </summary>
        [Range(0, 10_000)]
        public int StartCostCpuMs { get; set; } = 800;

        /// <summary>
        /// CPU a warm sandbox gets while it starts — Node, the runtime and the function's own
        /// imports — before it is dropped to the run limit at <c>ready</c>, ahead of its first call.
        /// The work is the same ~0.9 CPU-seconds either way, done in ~1 s instead of ~9.5 s at the
        /// 100m limit (measured 2026-10-06 on a real function under gVisor: 0.1 CPU 9.2–9.9 s,
        /// 1 CPU 0.92–1.0 s, 2 CPU 0.74–0.83 s). No call ever runs boosted: a sandbox whose drop
        /// cannot be confirmed is destroyed. Starts stay admitted by the host budget
        /// (<see cref="StartCostCpuMs"/>, <see cref="StartsPerSecondPerCore"/>), so a host without
        /// room queues the start like any other. <c>RUNNER__StartBoostMillicores</c>; 0 turns it off.
        /// Decided 2026-10-06 (start-up only; every call stays at the run limit).
        /// </summary>
        [Range(0, 8000)]
        public int StartBoostMillicores { get; set; } = 1000;

        /// <summary>
        /// Container starts per second per core this host may make, as
        /// <c>RUNNER__StartsPerSecondPerCore</c>. Together with <see cref="StartCostCpuMs"/> this
        /// is the CPU admission sets aside for starting sandboxes; a start over the rate waits on
        /// the queue like any other work that cannot run yet.
        /// </summary>
        [Range(0.01, 100.0)]
        public double StartsPerSecondPerCore { get; set; } = 1.0;

        /// <summary>
        /// How long after a call's answer leftover work may still finish before the call counts as
        /// dirty, as <c>RUNNER__CleanGraceMs</c>. Passed into the sandbox as
        /// <c>BLOCKS_CLEAN_GRACE_MS</c>. A clean call does not wait for it at all; only a call that
        /// left something running pays it, once, before its sandbox is judged.
        /// </summary>
        [Range(1, 10_000)]
        public int CleanGraceMs { get; set; } = 200;

        /// <summary>
        /// How long handing a call's envelope to a warm sandbox's stdin may take, as
        /// <c>RUNNER__WarmWriteTimeoutMs</c>. A healthy sandbox takes it in well under a
        /// millisecond; one whose event loop the tenant wedged stops reading, and without this
        /// bound the write — and with it this runner's whole run loop — would wait for ever. On
        /// expiry the sandbox is killed and the run served on a fresh one.
        /// </summary>
        [Range(100, 60_000)]
        public int WarmWriteTimeoutMs { get; set; } = 5000;

        /// <summary>Consume run jobs. Off turns this host into a build-only runner.</summary>
        public bool ProcessRuns { get; set; } = true;

        /// <summary>Consume build jobs.</summary>
        public bool ProcessBuilds { get; set; } = true;
    }
}
