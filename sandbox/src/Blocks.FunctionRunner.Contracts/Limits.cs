namespace Blocks.FunctionRunner.Contracts
{
    /// <summary>
    /// The platform ceilings. They are not configuration: the runner clamps whatever the
    /// control plane sends to these values, so a mistake upstream cannot widen a sandbox.
    /// <para>See <c>plan/DECISIONS.md</c>; changing a value here is a platform decision.</para>
    /// </summary>
    public static class Ceilings
    {
        /// <summary>
        /// Every sandbox gets exactly this much CPU — it is a fixed allocation, not a ceiling a
        /// function may choose under. 100 millicores is where Node's cold start stops dominating
        /// the request: each call is a fresh container, so boot and module import are paid every
        /// time and they are pure CPU. Lower buys nothing either, because admission never counts
        /// CPU (see HostBudget) — a smaller share would throttle the function without freeing a
        /// slot for anyone else.
        /// </summary>
        public const int CpuMillicores = 100;

        public const long MemoryBytes = 128L * 1024 * 1024;
        public const int PidLimit = 64;

        /// <summary>
        /// The least memory a sandbox may be given. Docker refuses a container under ~6 MB, so a
        /// request of a few bytes used to clamp "successfully" and then fail at create as a
        /// SANDBOX_START_FAILED the tenant could do nothing about; 32 MB is where Node boots and
        /// runs a small function under gVisor with headroom. Mirrored by the control plane's
        /// minimum for <c>FunctionLimits.MemoryMb</c>; the two move together.
        /// </summary>
        public const long MinMemoryBytes = 32L * 1024 * 1024;

        /// <summary>
        /// The least PIDs a sandbox may be given. Under runsc the limit also counts gVisor's own
        /// host-side processes (the sentry and its sidecars), so a small value does not constrain
        /// the function — it stops the sandbox from being created at all ("resource temporarily
        /// unavailable"). 32 is the measured floor at which Node starts and runs async work.
        /// </summary>
        public const int MinPidLimit = 32;
        public const long TmpfsBytes = 64L * 1024 * 1024;
        /// <summary>
        /// The hard ceiling on one run's wall clock. Raised from 60s to 90s for orchestration
        /// functions that fan out to several third-party APIs, where 60s was not a safety limit
        /// but an arbitrary one; 90s keeps a wedged run from holding a sandbox slot for minutes.
        /// This is the value that actually holds: the control plane clamps to its own copy of the
        /// same number, and this one clamps again, so the two must move together — a control plane
        /// allowing more than the runner does silently loses the difference to a killed sandbox.
        /// </summary>
        public const int TimeoutSeconds = 30;

        /// <summary>Kept equal to <see cref="TimeoutSeconds"/>: there is one value, not a range.</summary>
        public const int DefaultTimeoutSeconds = TimeoutSeconds;
        public const long InputBytes = 1024 * 1024;
        public const long ResultBytes = 5L * 1024 * 1024;
        public const long LogBytes = 1024 * 1024;
        public const int LogLines = 10_000;

        /// <summary>
        /// Per-function concurrent runs: a range of 1–25, defaulting to 2.
        /// <para>
        /// The ceiling was 5, which quietly capped one function at about 375 runs a minute at the
        /// 800 ms a typical run takes — a throughput limit dressed up as a safety limit. It is
        /// neither a memory nor a CPU bound: those are <see cref="HostBudget"/>'s job and it
        /// enforces them across every function on the host at once, so a higher number here only
        /// lets one function use capacity that would otherwise sit idle. Runs over the limit queue.
        /// </para>
        /// <para>
        /// It is mirrored by <c>FunctionLimits.Ceiling.MaxConcurrency</c> in the control plane and
        /// <c>DEFAULT_LIMITS_OPTIONS.maxConcurrency</c> in the client; the three move together.
        /// </para>
        /// </summary>
        public const int MinFunctionConcurrency = 1;
        public const int MaxFunctionConcurrency = 10;

        /// <summary>Kept equal to <see cref="MaxFunctionConcurrency"/>: one value, not a range.</summary>
        public const int DefaultFunctionConcurrency = MaxFunctionConcurrency;

        /// <summary>uid and gid the function process runs as inside the sandbox.</summary>
        public const int SandboxUid = 10001;

        /// <summary>
        /// The only container runtime tenant code may execute under, compiled in rather than
        /// configured. <c>RunnerOptions.Runtime</c> is bound from Genesis configuration like
        /// everything else, so without a fixed value to check it against, a line in
        /// <c>runner.env</c> or the <c>blocks-secret-function-runner</c> document could point it
        /// at <c>runc</c> and every sandbox on the host would quietly drop onto the host kernel
        /// while the runner still reported healthy. The startup guard refuses to claim work when
        /// the two disagree.
        /// </summary>
        public const string SandboxRuntime = "runsc";

        /// <summary>
        /// PID ceiling for a build sandbox. Far above a function's 64 because npm forks a
        /// process per package for a large install and this is a budget, not a boundary — the
        /// boundary is gVisor, which a build gets for the same reason a run does.
        /// </summary>
        public const int BuildPidLimit = 512;

        /// <summary>
        /// Writable scratch inside a build sandbox. npm unpacks tarballs through /tmp, so the
        /// 64 MB a function gets is not enough for a realistic dependency tree.
        /// </summary>
        public const long BuildTmpfsBytes = 512L * 1024 * 1024;
    }

    /// <summary>
    /// The effective limits for one run, already clamped. Construct only through
    /// <see cref="Clamp"/> so an unclamped set cannot reach the sandbox.
    /// </summary>
    public sealed record RunLimits
    {
        private RunLimits() { }

        public int CpuMillicores { get; private init; }
        public long MemoryBytes { get; private init; }
        public int PidLimit { get; private init; }
        public long TmpfsBytes { get; private init; }
        public int TimeoutSeconds { get; private init; }
        public int FunctionConcurrency { get; private init; }

        /// <summary>Nanocpus, the unit Docker's HostConfig expects.</summary>
        public long NanoCpus => CpuMillicores * 1_000_000L;

        /// <summary>Memory-swap is always equal to memory: a sandbox may never swap.</summary>
        public long MemorySwapBytes => MemoryBytes;

        /// <summary>
        /// ~75 % of the memory ceiling, so V8 throws a catchable heap error before the cgroup
        /// OOM killer where it can. The cgroup remains the hard boundary.
        /// </summary>
        public int MaxOldSpaceMb => (int)(MemoryBytes / 1024 / 1024 * 75 / 100);

        /// <summary>The default profile, used when the control plane sends nothing.</summary>
        public static RunLimits Default => Clamp(null, null, null, null, null, null);

        /// <summary>
        /// Returns the platform profile. Every argument is accepted and ignored.
        /// <para>
        /// The parameters remain so the wire format, the control plane's copy of it and every
        /// existing call site keep compiling and behaving — what changed is that none of the values
        /// are negotiable any more. A sandbox gets <see cref="Ceilings.CpuMillicores"/>,
        /// <see cref="Ceilings.MemoryBytes"/> and <see cref="Ceilings.TimeoutSeconds"/>, whatever
        /// arrives.
        /// </para>
        /// </summary>
        public static RunLimits Clamp(
            int? cpuMillicores,
            long? memoryBytes,
            int? pidLimit,
            long? tmpfsBytes,
            int? timeoutSeconds,
            int? functionConcurrency)
        {
            return new RunLimits
            {
                // Every dimension is fixed, not clamped. The arguments are still accepted so the
                // wire format and the control plane's copy of it keep working unchanged, and then
                // ignored: one profile for every sandbox is what makes a slot mean the same thing
                // whoever is running, and it is enforced here as well as upstream so a mistake in
                // the control plane cannot widen a sandbox.
                CpuMillicores = Ceilings.CpuMillicores,
                MemoryBytes = Ceilings.MemoryBytes,
                PidLimit = Ceilings.PidLimit,
                TmpfsBytes = Ceilings.TmpfsBytes,
                TimeoutSeconds = Ceilings.TimeoutSeconds,
                FunctionConcurrency = Ceilings.MaxFunctionConcurrency,
            };
        }
    }
}
