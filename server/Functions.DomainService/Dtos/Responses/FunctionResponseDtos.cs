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
        public DateTime? LastRunAt { get; set; }
        public DateTime? LastDeployedAt { get; set; }
        public DateTime LastUpdatedDate { get; set; }

        public static FunctionSummaryDto From(
            FunctionEntity function, int? activeVersionNumber, FunctionRunStatsEntity? stats) => new()
        {
            Id = function.ItemId,
            Name = function.Name,
            Status = function.Status.ToString(),
            IsDirty = function.IsDirty,
            ActiveVersionNumber = activeVersionNumber,
            TotalRuns = stats?.TotalRuns ?? 0,
            LastRunAt = stats?.LastRunAt,
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
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        public static FunctionVersionSummaryDto From(FunctionVersionEntity version) => new()
        {
            Id = version.ItemId,
            Number = version.Number,
            ImageDigest = version.ImageDigest,
            Note = version.Note,
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
        public int? ExitCode { get; set; }
        public bool LogsTruncated { get; set; }
        public List<RunAttempt> Attempts { get; set; } = [];
        public List<OutputActionResult> OutputResults { get; set; } = [];

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
            ExitCode = run.ExitCode,
            LogsTruncated = run.LogsTruncated,
            Attempts = run.Attempts,
            OutputResults = run.OutputResults,
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
    }

    /// <summary>Static platform ceilings and defaults, for the editor's limits form.</summary>
    public sealed class FunctionLimitsOptionsDto
    {
        public int CeilingCpuMillicores { get; set; } = FunctionLimits.Ceiling.CpuMillicores;
        public int CeilingMemoryMb { get; set; } = FunctionLimits.Ceiling.MemoryMb;
        public int CeilingTimeoutSeconds { get; set; } = FunctionLimits.Ceiling.TimeoutSeconds;
        public int MinConcurrency { get; set; } = FunctionLimits.Ceiling.MinConcurrency;
        public int MaxConcurrency { get; set; } = FunctionLimits.Ceiling.MaxConcurrency;
        public int DefaultCpuMillicores { get; set; } = FunctionLimits.Ceiling.DefaultCpuMillicores;
        public int DefaultMemoryMb { get; set; } = FunctionLimits.Ceiling.DefaultMemoryMb;
        public int DefaultTimeoutSeconds { get; set; } = FunctionLimits.Ceiling.DefaultTimeoutSeconds;
        public int DefaultConcurrency { get; set; } = FunctionLimits.Ceiling.DefaultConcurrency;

        /// <summary>
        /// Requests-per-minute/day are modelled and enforced (DECISIONS.md) but hidden in the
        /// interface unless this is true — which is only ever true when
        /// <c>Functions:RateLimits:ShowInUi</c> is switched on, independently of whether
        /// enforcement itself is switched on.
        /// </summary>
        public bool ShowRateLimits { get; set; }
    }
}
