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

        public const long MemoryBytes = 200L * 1024 * 1024;
        public const int PidLimit = 64;
        public const long TmpfsBytes = 64L * 1024 * 1024;
        /// <summary>
        /// The hard ceiling on one run's wall clock. Raised from 60s to 90s for orchestration
        /// functions that fan out to several third-party APIs, where 60s was not a safety limit
        /// but an arbitrary one; 90s keeps a wedged run from holding a sandbox slot for minutes.
        /// This is the value that actually holds: the control plane clamps to its own copy of the
        /// same number, and this one clamps again, so the two must move together — a control plane
        /// allowing more than the runner does silently loses the difference to a killed sandbox.
        /// </summary>
        public const int TimeoutSeconds = 90;
        public const int DefaultTimeoutSeconds = 10;
        public const long InputBytes = 1024 * 1024;
        public const long ResultBytes = 5L * 1024 * 1024;
        public const long LogBytes = 1024 * 1024;
        public const int LogLines = 10_000;

        /// <summary>Per-function concurrent runs: a range of 1–5, defaulting to 2.</summary>
        public const int MinFunctionConcurrency = 1;
        public const int MaxFunctionConcurrency = 5;
        public const int DefaultFunctionConcurrency = 2;

        /// <summary>Sandboxes admitted at once on the 4-vCPU reference VM.</summary>
        public const int DefaultHostAdmission = 10;

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
        /// Clamps a requested profile to the ceilings. Every argument is optional; a null, zero
        /// or negative value falls back to the ceiling (or, for timeout, to the 10 s default).
        /// A value above a ceiling is silently reduced — the caller does not get to argue.
        /// <para>
        /// <paramref name="cpuMillicores"/> is the exception: it is ignored entirely and every
        /// sandbox receives <see cref="Ceilings.CpuMillicores"/>. See that constant for why.
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
                // Not clamped — fixed. The argument is accepted so the wire format and the
                // control plane's copy of it keep working, and then ignored: CPU is the one
                // dimension a function does not get to choose.
                CpuMillicores = Ceilings.CpuMillicores,
                MemoryBytes = ClampLong(memoryBytes, 1, Ceilings.MemoryBytes, Ceilings.MemoryBytes),
                PidLimit = ClampInt(pidLimit, 1, Ceilings.PidLimit, Ceilings.PidLimit),
                TmpfsBytes = ClampLong(tmpfsBytes, 1, Ceilings.TmpfsBytes, Ceilings.TmpfsBytes),
                TimeoutSeconds = ClampInt(timeoutSeconds, 1, Ceilings.TimeoutSeconds, Ceilings.DefaultTimeoutSeconds),
                FunctionConcurrency = ClampInt(
                    functionConcurrency,
                    Ceilings.MinFunctionConcurrency,
                    Ceilings.MaxFunctionConcurrency,
                    Ceilings.DefaultFunctionConcurrency),
            };
        }

        private static int ClampInt(int? value, int min, int max, int fallback)
        {
            if (value is null || value <= 0) return fallback;
            return value.Value < min ? min : value.Value > max ? max : value.Value;
        }

        private static long ClampLong(long? value, long min, long max, long fallback)
        {
            if (value is null || value <= 0) return fallback;
            return value.Value < min ? min : value.Value > max ? max : value.Value;
        }
    }
}
