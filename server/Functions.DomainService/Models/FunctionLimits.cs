using MongoDB.Bson.Serialization.Attributes;
using Functions.DomainService.Enums;

namespace Functions.DomainService.Models
{
    /// <summary>
    /// Per-run resource limits.
    /// <para>
    /// <b>Not configurable.</b> Every function gets exactly the same profile: 100 millicores,
    /// 128 MB, 30 seconds, 10 concurrent runs. The fields remain on the model so stored documents
    /// written when they were editable still deserialize, but nothing reads them — <see cref="Clamp"/>
    /// discards whatever they hold and returns the platform profile, and every consumer goes
    /// through <see cref="Clamp"/> before a sandbox is created.
    /// </para>
    /// <para>
    /// One profile for everyone is what makes capacity predictable: a host's slot count means the
    /// same thing whoever is running, admission can count slots rather than weigh them, and no
    /// tenant can make their own runs cheaper or more expensive than anyone else's.
    /// </para>
    /// </summary>
    public class FunctionLimits
    {
        /// <summary>Ignored. <see cref="Ceiling.CpuMillicores"/> is what a run gets.</summary>
        public int CpuMillicores { get; set; } = Ceiling.CpuMillicores;

        /// <summary>Ignored. <see cref="Ceiling.MemoryMb"/> is what a run gets.</summary>
        public int MemoryMb { get; set; } = Ceiling.MemoryMb;

        /// <summary>Ignored. <see cref="Ceiling.TimeoutSeconds"/> is what a run gets.</summary>
        public int TimeoutSeconds { get; set; } = Ceiling.TimeoutSeconds;

        /// <summary>Ignored. <see cref="Ceiling.Concurrency"/> runs of one function at a time.</summary>
        public int Concurrency { get; set; } = Ceiling.Concurrency;

        /// <summary>
        /// Requests per minute. <c>null</c> means unlimited, which is the default and the only
        /// value V1 uses: the field exists in the model and API but is hidden in the interface
        /// and disabled by configuration, so nothing is ever refused for volume.
        /// </summary>
        public int? RequestsPerMinute { get; set; }

        /// <inheritdoc cref="RequestsPerMinute"/>
        public int? RequestsPerDay { get; set; }

        /// <summary>
        /// The one profile every function runs under. Not ceilings a function may choose under —
        /// these are the values, and the runner holds its own copy of each in
        /// <c>Blocks.FunctionRunner.Contracts.Ceilings</c> and applies them again. The two must
        /// move together; where they differ the runner wins, by construction.
        /// </summary>
        public static class Ceiling
        {
            /// <summary>
            /// Every sandbox gets exactly this. 100 millicores is where Node's cold start stops
            /// dominating the request: each call is a fresh container, so boot and module import
            /// are paid every time and they are pure CPU.
            /// </summary>
            public const int CpuMillicores = 100;

            /// <summary>
            /// Every sandbox gets exactly this. The runner gives V8 75 % of it as old-space heap
            /// (96 MB), and Node's own baseline is ~40-50 MB, so a function with a few dependencies
            /// has modest room. Held low deliberately: it is what lets one host carry many
            /// concurrent sandboxes, and capacity is the scarce thing here.
            /// </summary>
            public const int MemoryMb = 128;

            /// <summary>
            /// The hard wall clock for one run. A function that needs longer is not a function —
            /// it is a job, and it should fan out into several runs rather than hold a slot.
            /// </summary>
            public const int TimeoutSeconds = 30;

            /// <summary>
            /// Concurrent runs of one function, across the whole fleet (the runner holds the count
            /// in Redis, so adding hosts does not raise it). Runs beyond it queue; nothing is
            /// refused for concurrency.
            /// </summary>
            public const int Concurrency = 10;

            /// <summary>
            /// Attempts for a failed run, including the first. 2 means one retry. Fixed, because
            /// whether a retry is safe depends on the function and the platform cannot know —
            /// one is a reasonable allowance for a transient failure without making a
            /// side-effecting function run three times.
            /// </summary>
            public const int Attempts = 2;

            public const int PidLimit = 64;
            public const int TmpfsMb = 64;
            public const long InputBytes = 1024 * 1024;
            public const long ResultBytes = 5L * 1024 * 1024;
            public const long LogBytes = 1024 * 1024;
            public const int LogLines = 10_000;

            public const string RuntimeId = "node24";
        }

        /// <summary>
        /// The platform profile, whatever this instance holds.
        /// <para>
        /// Every consumer calls this before a run — deployment, the envelope builder and the
        /// invocation service — so a document stored when these were editable, or a request that
        /// still sends them, changes nothing about what actually executes.
        /// </para>
        /// </summary>
        public FunctionLimits Clamp() => new()
        {
            CpuMillicores = Ceiling.CpuMillicores,
            MemoryMb = Ceiling.MemoryMb,
            TimeoutSeconds = Ceiling.TimeoutSeconds,
            Concurrency = Ceiling.Concurrency,

            // Rate limiting is off by platform decision; the fields stay so an older document
            // deserializes, and null is what every code path already reads as "unlimited".
            RequestsPerMinute = null,
            RequestsPerDay = null,
        };
    }

    /// <summary>
    /// How a failed run is retried. Attempts include the first, so 2 means one retry.
    /// <para>
    /// <b>Not configurable</b>, like <see cref="FunctionLimits"/>. <see cref="Fixed"/> is the one
    /// policy, and the fields stay settable only so stored documents deserialize.
    /// </para>
    /// </summary>
    public class RetryPolicy
    {
        public int Attempts { get; set; } = FunctionLimits.Ceiling.Attempts;
        public BackoffKind Backoff { get; set; } = BackoffKind.Fixed;
        public int InitialDelaySeconds { get; set; } = 5;
        public int MaxDelaySeconds { get; set; } = 300;

        /// <summary>
        /// The policy every function retries under: one retry, five seconds later.
        /// <para>
        /// The wait is deliberate. A retry fired the instant the first attempt failed almost always
        /// meets the same condition — the upstream still down, the dependency still cold — so it
        /// spends an attempt to learn nothing.
        /// </para>
        /// </summary>
        public static RetryPolicy Fixed => new()
        {
            Attempts = FunctionLimits.Ceiling.Attempts,
            Backoff = BackoffKind.Fixed,
            InitialDelaySeconds = 5,
            MaxDelaySeconds = 300,
        };

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
    /// <remarks>
    /// <b>Rolling-deploy safety.</b> Extra elements are ignored, and every field added after the
    /// first release is written only when it differs from its default
    /// (<c>[BsonIgnoreIfDefault]</c> / <c>[BsonIgnoreIfNull]</c>). An untouched function's stored
    /// document therefore stays byte-identical, and an Api or Worker pod still running the previous
    /// build — during a rolling deploy, or after a rollback — keeps reading it instead of throwing
    /// on an element its class map does not know (which would fail production workflow steps).
    /// </remarks>
    [BsonIgnoreExtraElements]
    public class TriggerConfig
    {
        public bool HttpEnabled { get; set; } = true;

        /// <summary>
        /// GET or POST — one per function, like a proxy route's method. The public route is
        /// registered for both verbs and refuses the other one with 405, so a handler never has
        /// to branch on <c>input.method</c> unless it wants to.
        /// </summary>
        public HttpTriggerMethod HttpMethod { get; set; } = HttpTriggerMethod.Post;
        public AuthMode AuthMode { get; set; } = AuthMode.Token;
        public List<string> Roles { get; set; } = [];
        public List<string> Permissions { get; set; } = [];
        public MatchMode RoleMatch { get; set; } = MatchMode.Any;
        public MatchMode PermissionMatch { get; set; } = MatchMode.Any;

        /// <summary>
        /// OR / AND between the two lists once both are configured — the one toggle the "Restrict
        /// further" panel shows, exactly as the proxy's access card does. Each list's own any/all
        /// is <see cref="RoleMatch"/> / <see cref="PermissionMatch"/>. Defaults to Or, which is
        /// what the interface has always displayed for a function with no stored value.
        /// </summary>
        public AccessCombine Combine { get; set; } = AccessCombine.Or;

        /// <summary>Invocable from a workflow node.</summary>
        public bool WorkflowEnabled { get; set; } = true;

        /// <summary>
        /// The verbs the public route answers, from GET, POST, PUT, PATCH and DELETE. Null — what
        /// every document stored before this field existed reads as, and what an empty list is
        /// normalised to on save — means the single legacy <see cref="HttpMethod"/>, so a function
        /// nobody has touched keeps answering exactly the one verb it always did. Never written to
        /// Mongo while null. Read the effective set through
        /// <c>FunctionHttpInputBuilder.AllowedVerbs</c>, never this list directly.
        /// </summary>
        [BsonIgnoreIfNull]
        public List<string>? HttpMethods { get; set; }

        /// <summary>
        /// Opt-in to running this version in a reused ("warm") sandbox — same tenant, function and
        /// version only, one call at a time (sandbox/REUSE.md). Off by default: module-level state
        /// surviving between calls is a contract the tenant has to accept knowingly, and a run
        /// entry without <c>reuse=1</c> takes exactly the one-sandbox-per-run path it always has.
        /// Snapshotted into the version on deploy like every other trigger setting, so turning it
        /// on in the editor changes nothing until the next deploy. Only HTTP invocations use it
        /// (see <c>FunctionInvocationService</c>). Not written while false.
        /// </summary>
        [BsonIgnoreIfDefault]
        public bool ReuseSandbox { get; set; }

        /// <summary>
        /// <see cref="ResponseModes.Sync"/> holds a public HTTP request for a bounded time and answers
        /// with the function's own result. Anything else — null above all, which is the stored
        /// default and what "async" is normalised to on save — is today's 202 + poll token. A
        /// string rather than an enum so the wire value is exactly what the console sends. Not
        /// written while null.
        /// </summary>
        [BsonIgnoreIfNull]
        public string? ResponseMode { get; set; }

        /// <summary>True only for <see cref="ResponseModes.Sync"/>; null and "async" both mean async.</summary>
        public static bool IsSync(string? responseMode) =>
            string.Equals(responseMode, ResponseModes.Sync, StringComparison.Ordinal);

        /// <summary>The two values <see cref="ResponseMode"/> may hold.</summary>
        public static class ResponseModes
        {
            public const string Async = "async";
            public const string Sync = "sync";
        }
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
    /// <remarks>Rolling-deploy safe the same way as <see cref="TriggerConfig"/>.</remarks>
    [BsonIgnoreExtraElements]
    public class FunctionSource
    {
        public string IndexJs { get; set; } = string.Empty;
        public string PackageJson { get; set; } = string.Empty;

        /// <summary>
        /// Retained so stored documents and existing API callers keep round-tripping, but no
        /// longer used for anything: builds resolve package.json fresh and no lockfile is sent to
        /// the builder. It stays in the source hash because removing a field from that hash would
        /// change every existing hash and show every function as having unsaved changes.
        /// </summary>
        public string? LockJson { get; set; }

        /// <summary>
        /// Lets the dependency install run npm lifecycle scripts (install / postinstall), which
        /// native packages such as bcrypt or sharp need to compile or fetch their binary. Off by
        /// default: dependencies install with --ignore-scripts, as before. On is safe in the same
        /// way running the function is: the install happens inside a gVisor sandbox, and a runner
        /// operator can still refuse it host-wide (RUNNER__DenyPrivateScriptsOnBuild). Part of the
        /// source, not of the trigger, because it changes what the build produces — so it is in
        /// the source and manifest hashes (only when on, so no existing hash changes).
        /// </summary>
        [BsonIgnoreIfDefault]
        public bool AllowInstallScripts { get; set; }
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
