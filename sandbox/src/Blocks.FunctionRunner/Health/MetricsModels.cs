using System.Text.Json;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>One consumer-group reading of a stream. Null where Redis could not say.</summary>
    public sealed record StreamSample(long? Length, long? Pending, long? Lag);

    /// <summary>Warm sandbox counts, as the heartbeat reports them.</summary>
    public sealed record WarmSample(int Total, int Busy, int Idle);

    /// <summary>What the kernel says about this machine. Any field is null when it could not be read.</summary>
    public sealed record HostStats(
        long? MemTotalMb,
        long? MemAvailableMb,
        long? AnonMb,
        long? CacheMb,
        long? SwapTotalMb,
        long? SwapUsedMb,
        double? CpuPercent,
        double? Load1,
        double? DiskPercent);

    /// <summary>What Docker and the containerd shims say. Null when Docker could not be asked.</summary>
    public sealed record DockerStats(int? Running, int? Containers, int? Shims, int? OrphanShims);

    /// <summary>One line of <c>samples-*.jsonl</c>.</summary>
    public sealed record MetricsSample(
        DateTimeOffset Ts,
        string RunnerId,
        bool Healthy,
        WarmSample Warm,
        IReadOnlyDictionary<string, StreamSample> Streams,
        HostStats Host,
        DockerStats Docker);

    /// <summary>One line of <c>events-*.jsonl</c> or <c>police-*.jsonl</c>.</summary>
    public sealed record MetricsEvent(
        DateTimeOffset Ts,
        string Kind,
        string Level,
        string Message,
        double? Value = null,
        double? Threshold = null);

    /// <summary>Content of <c>gc-last.json</c>.</summary>
    public sealed record GcLast(DateTimeOffset Ts, int Removed);

    /// <summary>The JSON shape shared by writer (runner) and readers (fnctl).</summary>
    public static class MetricsJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
        };
    }

    /// <summary>The stream names a sample carries, one per queue the runner consumes.</summary>
    public static class StreamNames
    {
        public const string Runs = "runs";
        public const string EventRuns = "eventRuns";
        public const string Tests = "tests";
        public const string Builds = "builds";

        public static readonly string[] All = [Runs, EventRuns, Tests, Builds];
    }
}
