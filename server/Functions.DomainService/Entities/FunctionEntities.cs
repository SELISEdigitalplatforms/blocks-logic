using Blocks.Genesis;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using MongoDB.Bson.Serialization.Attributes;

namespace Functions.DomainService.Entities
{
    /// <summary>
    /// A tenant's function: its editable source, its configuration, and a pointer to the
    /// immutable version currently serving traffic.
    /// <para>
    /// Stored in the tenant's own database, so there is no TenantId here — tenancy is the
    /// database, as everywhere else in blocks-logic. <c>ItemId</c> is the only name a function
    /// has: it addresses it in the management API, in a workflow's function step, and in the
    /// public <c>/api/fn/{functionId}</c> route.
    /// </para>
    /// </summary>
    [BsonIgnoreExtraElements]
    public class FunctionEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
        public FunctionStatus Status { get; set; } = FunctionStatus.Draft;

        /// <summary>The editor's working copy. Deploying snapshots it into a version.</summary>
        public FunctionSource Source { get; set; } = new();

        /// <summary>
        /// sha256 of the source. Compared against the active version's code hash to decide
        /// whether the editor has unsaved-to-production changes, and used as the build cache
        /// key so Test and Deploy share one image.
        /// </summary>
        public string SourceHash { get; set; } = string.Empty;

        public FunctionLimits Limits { get; set; } = new();
        public RetryPolicy Retry { get; set; } = new();
        public TriggerConfig Trigger { get; set; } = new();
        public List<OutputAction> OutputActions { get; set; } = [];
        public List<VariableBinding> Variables { get; set; } = [];

        /// <summary>ItemId of the active <see cref="FunctionVersionEntity"/>, or null while Draft.</summary>
        public string? ActiveVersionId { get; set; }

        public int LastVersionNumber { get; set; }
        public DateTime? LastDeployedAt { get; set; }


        /// <summary>True when the editor's source differs from what is deployed.</summary>
        [BsonIgnore]
        public bool IsDirty { get; set; }
    }

    /// <summary>
    /// An immutable snapshot of a function at deploy time.
    /// <para>
    /// Everything a run needs is copied in, not referenced: limits, variables, trigger and
    /// output actions. Editing the function afterwards must not change how an already-deployed
    /// version behaves, and rolling back must not resurrect a configuration the tenant has
    /// since changed.
    /// </para>
    /// </summary>
    [BsonIgnoreExtraElements]
    public class FunctionVersionEntity : BaseEntity
    {
        public string FunctionId { get; set; } = string.Empty;
        public int Number { get; set; }

        /// <summary>Digest-pinned image reference, e.g. <c>registry/fn/{id}@sha256:…</c>.</summary>
        public string ImageDigest { get; set; } = string.Empty;

        /// <summary>sha256 of the source this version was built from.</summary>
        public string CodeHash { get; set; } = string.Empty;

        /// <summary>sha256 of the manifest and lockfile, so a dependency change is a new image.</summary>
        public string ManifestHash { get; set; } = string.Empty;

        public FunctionSource Source { get; set; } = new();
        public FunctionLimits Limits { get; set; } = new();
        public RetryPolicy Retry { get; set; } = new();
        public TriggerConfig Trigger { get; set; } = new();
        public List<OutputAction> OutputActions { get; set; } = [];
        public List<VariableBinding> Variables { get; set; } = [];

        /// <summary>Resolved dependency list from the lockfile, as reported by the builder.</summary>
        public string? Packages { get; set; }

        public string? Note { get; set; }
        public string RuntimeId { get; set; } = FunctionLimits.Ceiling.RuntimeId;
    }

    /// <summary>One execution of a function version.</summary>
    [BsonIgnoreExtraElements]
    public class FunctionRunEntity : BaseEntity
    {
        public string FunctionId { get; set; } = string.Empty;
        public string? VersionId { get; set; }
        public int VersionNumber { get; set; }

        /// <summary>
        /// Kept on the run even though the database is the tenant, because the runner echoes it
        /// back on the results stream and the consumer matches on it.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        public RunStatus Status { get; set; } = RunStatus.Queued;
        public RunErrorCode ErrorCode { get; set; } = RunErrorCode.None;
        public string? ErrorMessage { get; set; }

        public InvokedByType InvokedBy { get; set; } = InvokedByType.Http;

        /// <summary>Workflow execution id, replayed run id, or null.</summary>
        public string? InvokedById { get; set; }

        /// <summary>The input as sent, capped at 1 MB by admission before the run is created.</summary>
        public string? Input { get; set; }

        /// <summary>The returned value, or null when the run did not produce one.</summary>
        public string? Result { get; set; }

        public int Attempt { get; set; } = 1;
        public int MaxAttempts { get; set; } = FunctionLimits.Ceiling.DefaultAttempts;

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? DurationMs { get; set; }
        public long? PeakMemoryBytes { get; set; }
        public int? ExitCode { get; set; }
        public string? RunnerId { get; set; }

        /// <summary>True when the log stream hit a ceiling and was cut short.</summary>
        public bool LogsTruncated { get; set; }

        public List<RunStage> Stages { get; set; } = [];
        public List<RunAttempt> Attempts { get; set; } = [];
        public List<OutputActionResult> OutputResults { get; set; } = [];

        /// <summary>
        /// Idempotency key handed to output actions, so a run delivered twice does not cause
        /// the side effect twice. Format <c>{runId}-{attempt}</c>.
        /// </summary>
        public string IdempotencyKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Run counters for one function, kept out of <see cref="FunctionEntity"/> on purpose: that
    /// document is replaced wholesale by Save and Update, which would overwrite counters written
    /// by runs in the meantime. <c>ItemId</c> is the function id, so the increment is a single
    /// upsert with no read first.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class FunctionRunStatsEntity : BaseEntity
    {
        public long TotalRuns { get; set; }
        public DateTime? LastRunAt { get; set; }
    }

    /// <summary>One log line from a run, in the order the sandbox emitted it.</summary>
    [BsonIgnoreExtraElements]
    public class FunctionRunLogEntity : BaseEntity
    {
        public string RunId { get; set; } = string.Empty;
        public string FunctionId { get; set; } = string.Empty;

        /// <summary>Monotonic within a run; the sandbox's stdout order is the truth.</summary>
        public int Seq { get; set; }

        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "info";
        public string Message { get; set; } = string.Empty;

        /// <summary>Structured payload as raw JSON, or null.</summary>
        public string? Data { get; set; }
    }

    /// <summary>An image build for a given source hash.</summary>
    [BsonIgnoreExtraElements]
    public class FunctionBuildEntity : BaseEntity
    {
        public string FunctionId { get; set; } = string.Empty;

        /// <summary>The build cache key: one successful build per source hash is reused.</summary>
        public string SourceHash { get; set; } = string.Empty;

        public BuildStatus Status { get; set; } = BuildStatus.Queued;
        public string? ImageDigest { get; set; }
        public string? Packages { get; set; }
        public string? Log { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long? DurationMs { get; set; }
    }

    /// <summary>An audit trail entry. Retained a year.</summary>
    [BsonIgnoreExtraElements]
    public class FunctionAuditEventEntity : BaseEntity
    {
        public string FunctionId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? ActorId { get; set; }
        public string? ActorEmail { get; set; }

        /// <summary>Free-form context as raw JSON. Never contains a secret value.</summary>
        public string? Detail { get; set; }
    }
}
