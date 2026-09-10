namespace Functions.DomainService.Enums
{
    /// <summary>Lifecycle of a function as the tenant sees it.</summary>
    public enum FunctionStatus
    {
        /// <summary>Created and editable, never deployed.</summary>
        Draft = 0,

        /// <summary>Has an active version and serves traffic.</summary>
        Live = 1,

        /// <summary>Deployed once but currently disabled; invocations are refused.</summary>
        Paused = 2,
    }

    /// <summary>
    /// Run lifecycle. The wire forms live in <see cref="Queue.FunctionQueueKeys"/>; these
    /// values are what is persisted and returned to the client.
    /// </summary>
    public enum RunStatus
    {
        Queued = 0,
        Claimed = 1,
        Starting = 2,
        Running = 3,
        OutputProcessing = 4,
        Succeeded = 5,
        Failed = 6,
        TimedOut = 7,
        Cancelled = 8,
        ResourceExceeded = 9,
        OutputFailed = 10,
    }

    /// <summary>Why a run failed, as reported by the runner or determined here.</summary>
    public enum RunErrorCode
    {
        None = 0,
        MemoryLimit = 1,
        PidLimit = 2,
        UserRuntimeError = 3,
        RuntimeStartFailed = 4,
        ResultTooLarge = 5,
        ResultNotSerializable = 6,
        ImagePullFailed = 7,
        TimedOut = 8,
        SandboxStartFailed = 9,

        /// <summary>An output action failed after the function itself had succeeded.</summary>
        OutputActionFailed = 10,
    }

    /// <summary>What caused a run. Carried into <c>ctx.run.invokedBy</c>.</summary>
    public enum InvokedByType
    {
        Http = 0,
        Workflow = 1,
        Test = 2,
        Replay = 3,
        Schedule = 4,
        Event = 5,
    }

    /// <summary>How an HTTP trigger authenticates its caller.</summary>
    public enum AuthMode
    {
        /// <summary>No authentication. The sandbox gets an anonymous, unauthenticated context.</summary>
        Public = 0,

        /// <summary>A tenant bearer token, optionally narrowed by roles and permissions.</summary>
        Token = 1,
    }

    /// <summary>Whether every listed role/permission is required, or any one of them.</summary>
    public enum MatchMode
    {
        Any = 0,
        All = 1,
    }

    /// <summary>Shape of the delay between run attempts.</summary>
    public enum BackoffKind
    {
        None = 0,
        Fixed = 1,
        Exponential = 2,
    }

    /// <summary>What to do with a successful run's result.</summary>
    public enum OutputActionKind
    {
        /// <summary>POST/PUT the result to an external endpoint.</summary>
        ExternalHttp = 0,

        /// <summary>
        /// Reserved. Visible but disabled in the interface: no platform proxy service was
        /// found (see the open questions in DECISIONS.md).
        /// </summary>
        BlocksProxy = 1,
    }

    /// <summary>Lifecycle of an image build.</summary>
    public enum BuildStatus
    {
        Queued = 0,
        Building = 1,
        Succeeded = 2,
        Failed = 3,
    }
}
