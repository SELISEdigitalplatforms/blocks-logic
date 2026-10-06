using System.Text.Json.Serialization;

namespace Blocks.FunctionRunner.Contracts
{
    /// <summary>A run job read from <see cref="RedisKeys.RunsStream"/>.</summary>
    public sealed record RunJob
    {
        public required string RunId { get; init; }
        public required string FunctionId { get; init; }
        public string? VersionId { get; init; }
        public string? TenantId { get; init; }

        /// <summary>The image to execute, pinned by digest.</summary>
        public required string Image { get; init; }

        /// <summary>
        /// The control plane's attempt number, starting at 1 and carried on the run entry. Part
        /// of the output-action idempotency key, so it is never derived from delivery count.
        /// </summary>
        public int Attempt { get; init; } = 1;

        /// <summary>
        /// How many times the stream has handed out this entry, starting at 1. Diagnostic only:
        /// more than one means the same attempt is being redelivered after a runner lost it.
        /// </summary>
        public int Deliveries { get; init; } = 1;

        /// <summary>
        /// The entry's protocol version. 2 and up means <c>env</c> carries secret references the
        /// runner resolves; 1 means the plaintext is already there (see <see cref="RedisKeys.RunProtocolVersion"/>).
        /// </summary>
        public int Protocol { get; init; } = RedisKeys.RunProtocolVersion;

        /// <summary>
        /// True for an editor test run. It changes which concurrency budget the run draws on:
        /// a test must never occupy a slot the function's deployed version needs to serve
        /// traffic. Never set from the wire — the test loop sets it on the job it builds.
        /// </summary>
        public bool IsTest { get; init; }

        /// <summary>
        /// The function opted in to sandbox reuse: the entry carried <c>reuse=1</c>
        /// (<see cref="RedisKeys.RunReuseField"/>). Only a request — the run is served by a warm
        /// sandbox only when this runner also has <c>SandboxReuse</c> on (sandbox/REUSE.md).
        /// </summary>
        public bool Reuse { get; init; }

        /// <summary>
        /// A short-lived, read-only URL for this version's build artifact, when the control plane
        /// sent one.
        /// <para>
        /// Present means this host may build the image itself from a shared base rather than pull it
        /// from a registry — which is what lets a host run a function it has never seen without the
        /// host that built it being involved. Absent means the old path: pull <see cref="Image"/>.
        /// </para>
        /// </summary>
        public string? ArtifactUrl { get; init; }

        /// <summary>
        /// SHA-256 the downloaded artifact must have.
        /// <para>
        /// The URL is signed, but signing says where the bytes came from, not that they are whole. A
        /// truncated or swapped artifact would otherwise be built into an image and executed, so the
        /// download is checked against this before anything is built from it.
        /// </para>
        /// </summary>
        public string? ArtifactSha256 { get; init; }

        /// <summary>
        /// The Api's own step times for this call (<c>function=35;version=34;…</c>), carried from the
        /// run entry to the result entry untouched, so the run record can show where all of a call's
        /// time went. Times only. Null from a control plane that does not send them.
        /// </summary>
        public string? ApiTimings { get; init; }

        /// <summary>How long the entry sat on the runs stream before this runner claimed it, in ms.</summary>
        public long? QueuedMs { get; init; }
    }

    /// <summary>A result written to <see cref="RedisKeys.ResultsStream"/> for the logic Worker.</summary>
    public sealed record RunResultMessage
    {
        public required string RunId { get; init; }

        /// <summary>
        /// Echoed from the job. blocks-logic is database-per-tenant, so a consumer holding only a
        /// runId cannot locate the record to update.
        /// </summary>
        public string? FunctionId { get; init; }

        /// <inheritdoc cref="FunctionId"/>
        public string? TenantId { get; init; }

        public required string Status { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public int? ExitCode { get; init; }
        public long DurationMs { get; init; }
        public long? PeakMemoryBytes { get; init; }
        public long? CpuUsageMs { get; init; }
        public required string RunnerId { get; init; }
        public required string StartedAt { get; init; }
        public required string CompletedAt { get; init; }

        /// <summary>Key of the stored result payload, or null when there is none.</summary>
        public string? ResultKey { get; init; }

        /// <summary>Key of the stored log list, or null when nothing was logged.</summary>
        public string? LogsKey { get; init; }

        /// <summary>True when logs hit the byte or line ceiling and were cut short.</summary>
        public bool Truncated { get; init; }

        public int Protocol { get; init; } = RedisKeys.ProtocolVersion;
    }

    /// <summary>A build job read from <see cref="RedisKeys.BuildsStream"/>.</summary>
    public sealed record BuildJob
    {
        public required string BuildId { get; init; }
        public required string FunctionId { get; init; }
        public string? TenantId { get; init; }
        public required string SourceKey { get; init; }
        public required string ImageRef { get; init; }
        public bool AllowScripts { get; init; }

        /// <summary>
        /// A test run's build: tagged <see cref="ImageRef"/> in the local Engine only, never
        /// pushed, and labelled so Image GC can reclaim it if the run that owns it never cleans up.
        /// </summary>
        public bool LocalOnly { get; init; }

        /// <summary>
        /// A short-lived, write-only URL for the one blob this build's artifact belongs in, when the
        /// control plane sent one.
        /// <para>
        /// Present means publish by uploading the build context rather than by pushing an image to a
        /// registry. The builder holds no storage credential of its own — this URL is the whole of
        /// its access, and it can write only this one blob.
        /// </para>
        /// </summary>
        public string? ArtifactUploadUrl { get; init; }

        public int Protocol { get; init; } = RedisKeys.ProtocolVersion;
    }

    /// <summary>A build result written to <see cref="RedisKeys.BuildResultsStream"/>.</summary>
    public sealed record BuildResultMessage
    {
        public required string BuildId { get; init; }
        public string? FunctionId { get; init; }

        /// <inheritdoc cref="RunResultMessage.TenantId"/>
        public string? TenantId { get; init; }

        public required string Status { get; init; }
        public string? ImageDigest { get; init; }

        /// <summary>
        /// SHA-256 of the artifact this build uploaded, when it uploaded one. Its presence is what
        /// tells the control plane the build published an artifact rather than a registry image.
        /// </summary>
        public string? ArtifactSha256 { get; init; }

        public string? Packages { get; init; }
        public string? Log { get; init; }
        public string? ErrorMessage { get; init; }
        public int Protocol { get; init; } = RedisKeys.ProtocolVersion;
    }

    /// <summary>The runner heartbeat, published to <see cref="RedisKeys.Runner"/>.</summary>
    public sealed record RunnerHeartbeat
    {
        public required string RunnerId { get; init; }
        public required string Version { get; init; }
        public int Active { get; init; }
        public int Capacity { get; init; }
        public bool GvisorOk { get; init; }
        public bool Healthy { get; init; }

        /// <summary>Warm (reusable) sandboxes alive on this runner; 0 with reuse off.</summary>
        public int WarmTotal { get; init; }

        /// <summary>Warm sandboxes serving a call right now.</summary>
        public int WarmBusy { get; init; }

        /// <summary>Warm sandboxes paused and waiting for a call (or starting).</summary>
        public int WarmIdle { get; init; }
        public string? Detail { get; init; }
        public required string ObservedAt { get; init; }
    }

    /// <summary>
    /// One line of the sandbox stdout protocol. <see cref="Type"/> is <c>log</c>,
    /// <c>truncated</c> or <c>result</c>.
    /// </summary>
    public sealed record SandboxEvent
    {
        [JsonPropertyName("t")] public string? Type { get; init; }
        [JsonPropertyName("ts")] public string? Timestamp { get; init; }
        [JsonPropertyName("level")] public string? Level { get; init; }
        [JsonPropertyName("msg")] public string? Message { get; init; }
        [JsonPropertyName("data")] public object? Data { get; init; }
        [JsonPropertyName("reason")] public string? Reason { get; init; }

        /// <summary>
        /// On a <c>started</c> line: the sandbox's own clock when the handler began, in Unix
        /// milliseconds. Absent on every other line, and on a runtime image that predates it.
        /// </summary>
        [JsonPropertyName("at")] public long? At { get; init; }
        [JsonPropertyName("ok")] public bool? Ok { get; init; }
        [JsonPropertyName("value")] public object? Value { get; init; }
        [JsonPropertyName("code")] public string? Code { get; init; }
        [JsonPropertyName("message")] public string? ErrorMessage { get; init; }
        [JsonPropertyName("stack")] public string? Stack { get; init; }
    }

    /// <summary>Sandbox process exit codes, defined by the bootstrap.</summary>
    public static class SandboxExit
    {
        public const int Ok = 0;
        public const int UserError = 10;
        public const int BootstrapError = 20;
        public const int Sigkill = 137;
        public const int Sigterm = 143;
    }
}
