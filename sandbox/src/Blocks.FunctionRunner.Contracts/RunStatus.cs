namespace Blocks.FunctionRunner.Contracts
{
    /// <summary>
    /// The run lifecycle from <c>plan/PROTOCOL.md</c>. Sent as the exact string on the wire, so
    /// the names are part of the contract; <see cref="RunStatuses"/> holds the wire forms.
    /// </summary>
    public enum RunStatus
    {
        Queued,
        Claimed,
        Starting,
        Running,
        OutputProcessing,
        Succeeded,
        Failed,
        TimedOut,
        Cancelled,
        ResourceExceeded,
        OutputFailed,
    }

    /// <summary>Wire forms of <see cref="RunStatus"/> and the mapping between the two.</summary>
    public static class RunStatuses
    {
        public const string Queued = "QUEUED";
        public const string Claimed = "CLAIMED";
        public const string Starting = "STARTING";
        public const string Running = "RUNNING";
        public const string OutputProcessing = "OUTPUT_PROCESSING";
        public const string Succeeded = "SUCCEEDED";
        public const string Failed = "FAILED";
        public const string TimedOut = "TIMED_OUT";
        public const string Cancelled = "CANCELLED";
        public const string ResourceExceeded = "RESOURCE_EXCEEDED";
        public const string OutputFailed = "OUTPUT_FAILED";

        public static string ToWire(RunStatus status) => status switch
        {
            RunStatus.Queued => Queued,
            RunStatus.Claimed => Claimed,
            RunStatus.Starting => Starting,
            RunStatus.Running => Running,
            RunStatus.OutputProcessing => OutputProcessing,
            RunStatus.Succeeded => Succeeded,
            RunStatus.Failed => Failed,
            RunStatus.TimedOut => TimedOut,
            RunStatus.Cancelled => Cancelled,
            RunStatus.ResourceExceeded => ResourceExceeded,
            RunStatus.OutputFailed => OutputFailed,
            _ => Failed,
        };

        /// <summary>True once no further transition is possible.</summary>
        public static bool IsTerminal(RunStatus status) => status is
            RunStatus.Succeeded or RunStatus.Failed or RunStatus.TimedOut or
            RunStatus.Cancelled or RunStatus.ResourceExceeded or RunStatus.OutputFailed;
    }

    /// <summary>Error codes from <c>plan/PROTOCOL.md</c>, as sent on the wire.</summary>
    public static class ErrorCodes
    {
        /// <summary>The cgroup memory ceiling killed the sandbox.</summary>
        public const string MemoryLimit = "MEMORY_LIMIT";

        /// <summary>The PID ceiling stopped the sandbox forking further.</summary>
        public const string PidLimit = "PID_LIMIT";

        /// <summary>The tenant's handler threw, or its module failed to load.</summary>
        public const string UserRuntimeError = "USER_RUNTIME_ERROR";

        /// <summary>The trusted bootstrap could not start the handler at all.</summary>
        public const string RuntimeStartFailed = "RUNTIME_START_FAILED";

        /// <summary>The returned value exceeded the 5 MB result ceiling.</summary>
        public const string ResultTooLarge = "RESULT_TOO_LARGE";

        /// <summary>The returned value could not be serialized (circular, BigInt, throwing toJSON).</summary>
        public const string ResultNotSerializable = "RESULT_NOT_SERIALIZABLE";

        /// <summary>The function image could not be pulled or its digest did not match.</summary>
        public const string ImagePullFailed = "IMAGE_PULL_FAILED";

        /// <summary>The soft deadline fired in the sandbox, or the runner hard-killed the container.</summary>
        public const string TimedOut = "TIMED_OUT";

        /// <summary>The sandbox could not be created — the host, not the tenant, is at fault.</summary>
        public const string SandboxStartFailed = "SANDBOX_START_FAILED";
    }
}
