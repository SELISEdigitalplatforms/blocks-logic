using Functions.DomainService.Enums;

namespace Functions.DomainService.Models
{
    /// <summary>
    /// Per-run resource limits as the tenant configures them.
    /// <para>
    /// These are a <i>request</i>. The Runner VM clamps every value to the platform ceiling
    /// again before it creates a sandbox, so nothing here can widen a sandbox — a mistake in
    /// this half cannot become a security problem in the other. <see cref="Clamp"/> exists so
    /// the interface shows the tenant the same numbers the runner will actually apply.
    /// </para>
    /// </summary>
    public class FunctionLimits
    {
        public int CpuMillicores { get; set; } = Ceiling.DefaultCpuMillicores;
        public int MemoryMb { get; set; } = Ceiling.DefaultMemoryMb;
        public int TimeoutSeconds { get; set; } = Ceiling.DefaultTimeoutSeconds;

        /// <summary>Concurrent runs of this function, 1–5. A scheduling limit, never a rejection.</summary>
        public int Concurrency { get; set; } = Ceiling.DefaultConcurrency;

        /// <summary>
        /// Requests per minute. <c>null</c> means unlimited, which is the default and the only
        /// value V1 uses: the field exists in the model and API but is hidden in the interface
        /// and disabled by configuration, so nothing is ever refused for volume.
        /// </summary>
        public int? RequestsPerMinute { get; set; }

        /// <inheritdoc cref="RequestsPerMinute"/>
        public int? RequestsPerDay { get; set; }

        /// <summary>The platform ceilings, mirrored from DECISIONS.md and the runner's own constants.</summary>
        public static class Ceiling
        {
            public const int CpuMillicores = 200;
            public const int MemoryMb = 300;
            public const int TimeoutSeconds = 60;
            public const int PidLimit = 64;
            public const int TmpfsMb = 64;
            public const long InputBytes = 1024 * 1024;
            public const long ResultBytes = 5L * 1024 * 1024;
            public const long LogBytes = 1024 * 1024;
            public const int LogLines = 10_000;

            public const int MinConcurrency = 1;
            public const int MaxConcurrency = 5;

            /// <summary>Defaults for a newly created function — deliberately below the ceilings.</summary>
            public const int DefaultCpuMillicores = 100;
            public const int DefaultMemoryMb = 192;
            public const int DefaultTimeoutSeconds = 10;
            public const int DefaultConcurrency = 2;
            public const int DefaultAttempts = 1;

            public const string RuntimeId = "node24";
        }

        /// <summary>
        /// Returns a copy with every value inside the platform ceilings. Mirrors what the
        /// runner does; if the two ever disagree the runner wins, by construction.
        /// </summary>
        public FunctionLimits Clamp() => new()
        {
            CpuMillicores = Math.Clamp(
                CpuMillicores <= 0 ? Ceiling.DefaultCpuMillicores : CpuMillicores, 1, Ceiling.CpuMillicores),
            MemoryMb = Math.Clamp(
                MemoryMb <= 0 ? Ceiling.DefaultMemoryMb : MemoryMb, 1, Ceiling.MemoryMb),
            TimeoutSeconds = Math.Clamp(
                TimeoutSeconds <= 0 ? Ceiling.DefaultTimeoutSeconds : TimeoutSeconds, 1, Ceiling.TimeoutSeconds),
            Concurrency = Math.Clamp(
                Concurrency <= 0 ? Ceiling.DefaultConcurrency : Concurrency,
                Ceiling.MinConcurrency, Ceiling.MaxConcurrency),
            RequestsPerMinute = RequestsPerMinute is > 0 ? RequestsPerMinute : null,
            RequestsPerDay = RequestsPerDay is > 0 ? RequestsPerDay : null,
        };
    }

    /// <summary>How a failed run is retried. Attempts include the first, so 1 means no retry.</summary>
    public class RetryPolicy
    {
        public int Attempts { get; set; } = FunctionLimits.Ceiling.DefaultAttempts;
        public BackoffKind Backoff { get; set; } = BackoffKind.None;
        public int InitialDelaySeconds { get; set; } = 5;
        public int MaxDelaySeconds { get; set; } = 300;

        /// <summary>Delay before <paramref name="attempt"/> (1-based; attempt 1 never waits).</summary>
        public TimeSpan DelayFor(int attempt)
        {
            if (attempt <= 1 || Backoff == BackoffKind.None) return TimeSpan.Zero;

            var seconds = Backoff == BackoffKind.Fixed
                ? InitialDelaySeconds
                : InitialDelaySeconds * Math.Pow(2, attempt - 2);

            return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelaySeconds));
        }
    }

    /// <summary>HTTP trigger configuration.</summary>
    public class TriggerConfig
    {
        public bool HttpEnabled { get; set; } = true;
        public AuthMode AuthMode { get; set; } = AuthMode.Token;
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
        public MatchMode RoleMatch { get; set; } = MatchMode.Any;
        public MatchMode PermissionMatch { get; set; } = MatchMode.Any;

        /// <summary>Invocable from a workflow node.</summary>
        public bool WorkflowEnabled { get; set; } = true;
    }

    /// <summary>Something to do with a successful run's result.</summary>
    public class OutputAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public OutputActionKind Kind { get; set; } = OutputActionKind.ExternalHttp;
        public bool Enabled { get; set; } = true;
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";

        /// <summary>
        /// Header values may contain <c>{{secret.NAME}}</c>. Substitution happens only in the
        /// Worker's output processor, never in the API and never inside a sandbox.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = [];

        /// <summary>Null sends the result JSON verbatim; otherwise a template with the same substitution.</summary>
        public string? BodyTemplate { get; set; }

        public int TimeoutSeconds { get; set; } = 30;
    }

    /// <summary>A non-secret configuration value exposed to the function as <c>ctx.env.KEY</c>.</summary>
    public class VariableBinding
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>The tenant's editable source. Hashed to decide whether a rebuild is needed.</summary>
    public class FunctionSource
    {
        public string IndexJs { get; set; } = string.Empty;
        public string PackageJson { get; set; } = string.Empty;
        public string? LockJson { get; set; }
    }

    /// <summary>One observed stage of a run, for the timeline in the interface.</summary>
    public class RunStage
    {
        public string Name { get; set; } = string.Empty;
        public DateTime At { get; set; }
        public long? DurationMs { get; set; }
    }

    /// <summary>One delivery attempt of a run.</summary>
    public class RunAttempt
    {
        public int Number { get; set; }
        public RunStatus Status { get; set; }
        public RunErrorCode ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? DurationMs { get; set; }
    }

    /// <summary>The outcome of one output action.</summary>
    public class OutputActionResult
    {
        public string ActionId { get; set; } = string.Empty;
        public OutputActionKind Kind { get; set; }
        public bool Ok { get; set; }
        public int? StatusCode { get; set; }
        public string? Error { get; set; }
        public long DurationMs { get; set; }
        public int Attempts { get; set; }
    }
}
