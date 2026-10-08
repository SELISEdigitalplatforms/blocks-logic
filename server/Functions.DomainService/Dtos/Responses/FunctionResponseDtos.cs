using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;

namespace Functions.DomainService.Dtos.Responses
{
    public sealed class FunctionSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public int? ActiveVersionNumber { get; set; }
        public long TotalRuns { get; set; }

        /// <summary>Runs in the last 24 hours — the list's "Runs 24 h" column.</summary>
        public long Runs24h { get; set; }

        /// <summary>The list's "Invoked by" column, without shipping the whole trigger config.</summary>
        public bool HttpEnabled { get; set; }
        public bool WorkflowEnabled { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime? LastDeployedAt { get; set; }
        public DateTime LastUpdatedDate { get; set; }

        public static FunctionSummaryDto From(
            FunctionEntity function, int? activeVersionNumber, long runs24h = 0) => new()
        {
            Id = function.ItemId,
            Name = function.Name,
            Status = function.Status.ToString(),
            IsDirty = function.IsDirty,
            ActiveVersionNumber = activeVersionNumber,
            TotalRuns = function.TotalRuns,
            Runs24h = runs24h,
            HttpEnabled = function.Trigger.HttpEnabled,
            WorkflowEnabled = function.Trigger.WorkflowEnabled,
            LastRunAt = function.LastRunAt,
            LastDeployedAt = function.LastDeployedAt,
            LastUpdatedDate = function.LastUpdatedDate,
        };
    }

    public sealed class FunctionDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public string IndexJs { get; set; } = string.Empty;
        public string PackageJson { get; set; } = string.Empty;
        public string? LockJson { get; set; }
        public bool AllowInstallScripts { get; set; }
        public FunctionLimits Limits { get; set; } = new();
        public RetryPolicy Retry { get; set; } = new();
        public TriggerConfig Trigger { get; set; } = new();
        public List<OutputAction> OutputActions { get; set; } = [];
        public List<VariableBinding> Variables { get; set; } = [];
        public string? ActiveVersionId { get; set; }
        public int? ActiveVersionNumber { get; set; }
        public int LastVersionNumber { get; set; }

        public static FunctionDetailDto From(FunctionEntity function) => new()
        {
            Id = function.ItemId,
            Name = function.Name,
            Description = function.Description,
            Status = function.Status.ToString(),
            IsDirty = function.IsDirty,
            IndexJs = function.Source.IndexJs,
            PackageJson = function.Source.PackageJson,
            LockJson = function.Source.LockJson,
            AllowInstallScripts = function.Source.AllowInstallScripts,
            Limits = function.Limits,
            Retry = function.Retry,
            Trigger = function.Trigger,
            OutputActions = function.OutputActions,
            Variables = function.Variables,
            ActiveVersionId = function.ActiveVersionId,
            LastVersionNumber = function.LastVersionNumber,
        };
    }

    public sealed class FunctionVersionSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public int Number { get; set; }
        public string ImageDigest { get; set; } = string.Empty;
        public string? Note { get; set; }

        /// <summary>Resolved dependencies of this version, as the builder reported them.</summary>
        public string? Packages { get; set; }

        /// <summary>Runs recorded against this version — retention still applies to the runs.</summary>
        public long RunCount { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        public static FunctionVersionSummaryDto From(FunctionVersionEntity version, long runCount = 0) => new()
        {
            Id = version.ItemId,
            Number = version.Number,
            ImageDigest = version.ImageDigest,
            Note = version.Note,
            Packages = version.Packages,
            RunCount = runCount,
            CreatedDate = version.CreatedDate,
            CreatedBy = version.CreatedBy ?? string.Empty,
        };
    }

    public sealed class RunSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string FunctionId { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string InvokedBy { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? DurationMs { get; set; }

        /// <summary>Peak RSS of the sandbox — the runs table shows it against the limit.</summary>
        public long? PeakMemoryBytes { get; set; }
        /// <summary>
        /// Warm (true) or cold (false) start, for the list's "· warm / · cold" marker; null for a run
        /// that did not use a reused sandbox. The detail DTO carries the rest of the sandbox report.
        /// </summary>
        public bool? Reused { get; set; }

        public static RunSummaryDto From(FunctionRunEntity run) => new()
        {
            Id = run.ItemId,
            FunctionId = run.FunctionId,
            VersionNumber = run.VersionNumber,
            Status = run.Status.ToString(),
            ErrorCode = run.ErrorCode == RunErrorCode.None ? null : run.ErrorCode.ToString(),
            InvokedBy = run.InvokedBy.ToString(),
            Attempt = run.Attempt,
            CreatedDate = run.CreatedDate,
            CompletedAt = run.CompletedAt,
            DurationMs = run.DurationMs,
            PeakMemoryBytes = run.PeakMemoryBytes,
            Reused = run.Reused,
        };
    }

    public sealed class RunDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string FunctionId { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string InvokedBy { get; set; } = string.Empty;
        public string? InvokedById { get; set; }
        public string? Input { get; set; }
        public string? Result { get; set; }
        public int Attempt { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? DurationMs { get; set; }
        public long? PeakMemoryBytes { get; set; }

        /// <summary>Total CPU time the sandbox consumed for the run, in milliseconds — a cumulative counter, not a percentage.</summary>
        public long? CpuUsageMs { get; set; }

        /// <summary>
        /// The wall ms <see cref="CpuUsageMs"/> was measured over when it is the handler's own window
        /// (host counter, handler start to result), so the two divide into millicores. Null when the
        /// CPU figure is a total that includes start-up — never divide that one by a duration.
        /// </summary>
        public long? CpuWindowMs { get; set; }
        public int? ExitCode { get; set; }
        public bool LogsTruncated { get; set; }
        public List<RunAttempt> Attempts { get; set; } = [];
        public List<OutputActionResult> OutputResults { get; set; } = [];

        /// <summary>Served by an already-running (warm) sandbox; null when the runner did not say.</summary>
        public bool? Reused { get; set; }

        /// <summary>Why the warm sandbox was destroyed after this run; null when it was kept, or the runner did not say.</summary>
        public string? DiscardReason { get; set; }

        /// <summary>Claim to envelope-on-stdin of a warm sandbox, in milliseconds; null when not reported.</summary>
        public long? HandoverMs { get; set; }

        /// <summary>Where the call's time went, step by step; null for a run from before timings existed.</summary>
        public List<RunTiming>? Timings { get; set; }

        public static RunDetailDto From(FunctionRunEntity run) => new()
        {
            Id = run.ItemId,
            FunctionId = run.FunctionId,
            VersionNumber = run.VersionNumber,
            Status = run.Status.ToString(),
            ErrorCode = run.ErrorCode == RunErrorCode.None ? null : run.ErrorCode.ToString(),
            ErrorMessage = run.ErrorMessage,
            InvokedBy = run.InvokedBy.ToString(),
            InvokedById = run.InvokedById,
            Input = run.Input,
            Result = run.Result,
            Attempt = run.Attempt,
            MaxAttempts = run.MaxAttempts,
            CreatedDate = run.CreatedDate,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            DurationMs = run.DurationMs,
            PeakMemoryBytes = run.PeakMemoryBytes,
            CpuUsageMs = run.CpuUsageMs,
            CpuWindowMs = run.CpuWindowMs,
            ExitCode = run.ExitCode,
            LogsTruncated = run.LogsTruncated,
            Attempts = run.Attempts,
            OutputResults = run.OutputResults,
            Reused = run.Reused,
            DiscardReason = run.DiscardReason,
            HandoverMs = run.HandoverMs,
            Timings = run.Timings,
        };
    }

    public sealed class RunLogLineDto
    {
        public int Seq { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "info";
        public string Message { get; set; } = string.Empty;
        public string? Data { get; set; }
    }

    /// <summary>What the invoke endpoint returns: 202 immediately, or the full result after waiting.</summary>
    public sealed class InvokeResultDto
    {
        public string RunId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Result { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Set instead of a run when the image was still building: there is nothing to invoke yet,
        /// so the caller polls <c>GetBuild</c> with this id and runs again once it succeeds. The
        /// editor's build-progress indicator is driven from here.
        /// </summary>
        public string? BuildId { get; set; }

        /// <summary>The build's status at the moment the request gave up waiting.</summary>
        public string? BuildStatus { get; set; }

        /// <summary>
        /// Set only on a 202 from the public <c>/api/fn</c> route: the one-time capability for
        /// <c>GET /api/fn/runs/{runId}/result</c> (header <c>x-poll-token</c>), so an anonymous
        /// caller can collect its result. Shown once; only its hash is kept. Omitted otherwise.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? PollToken { get; set; }

        /// <summary>
        /// Set by the public route's synchronous mode only: the caller waited, so a finished run is
        /// answered with the function's own response (<c>FunctionHttpResponseMapper</c>) rather
        /// than this DTO. Never serialized — the 202 a sync caller falls back to has to be the
        /// same bytes an async caller gets.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool RespondSynchronously { get; set; }

        /// <summary>
        /// Set by a sync wait the moment the run started streaming its answer (F-5): the caller is
        /// answered with the pieces as they come (<c>FunctionStreamReader</c>), not with this DTO.
        /// Never serialized.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Streaming { get; set; }
    }

    /// <summary>
    /// The one profile every function runs under.
    /// <para>
    /// These are not ceilings to choose under and not defaults to start from — they are the values,
    /// the same for every function. The editor reads them from here rather than holding its own copy,
    /// so the number on screen is always the number the runner applies.
    /// </para>
    /// </summary>
    public sealed class FunctionLimitsOptionsDto
    {
        public int CpuMillicores { get; set; } = FunctionLimits.Ceiling.CpuMillicores;
        public int MemoryMb { get; set; } = FunctionLimits.Ceiling.MemoryMb;
        public int TimeoutSeconds { get; set; } = FunctionLimits.Ceiling.TimeoutSeconds;
        public int Concurrency { get; set; } = FunctionLimits.Ceiling.Concurrency;
        public int Attempts { get; set; } = FunctionLimits.Ceiling.Attempts;
        public int RetryDelaySeconds { get; set; } = RetryPolicy.Fixed.InitialDelaySeconds;

        /// <summary>
        /// Requests-per-minute/day are modelled and enforced (DECISIONS.md) but hidden in the
        /// interface unless this is true — which is only ever true when
        /// <c>Functions:RateLimits:ShowInUi</c> is switched on, independently of whether
        /// enforcement itself is switched on.
        /// </summary>
        public bool ShowRateLimits { get; set; }
    }
}
