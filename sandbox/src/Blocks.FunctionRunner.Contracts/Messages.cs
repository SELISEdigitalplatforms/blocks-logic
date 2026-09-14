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

        /// <summary>Delivery attempt, starting at 1. Part of the output-action idempotency key.</summary>
        public int Attempt { get; init; } = 1;

        public int Protocol { get; init; } = RedisKeys.ProtocolVersion;
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
