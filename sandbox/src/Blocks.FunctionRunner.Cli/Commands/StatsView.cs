using System.Globalization;
using System.Text;
using Blocks.FunctionRunner.Health;

namespace Blocks.FunctionRunner.Cli.Commands
{
    /// <summary>Turns samples and events into the short text <c>fnctl stats</c>, <c>watch</c> and <c>events</c> print.</summary>
    internal static class StatsView
    {
        private static readonly TimeSpan Bucket = TimeSpan.FromMinutes(5);

        public static string Latest(MetricsSample sample, DateTimeOffset now)
        {
            var age = now - sample.Ts;
            var host = sample.Host;
            var text = new StringBuilder();
            text.AppendLine(FormattableString.Invariant($"Runner {sample.RunnerId}  healthy={(sample.Healthy ? "yes" : "NO")}  sample {(long)age.TotalSeconds} s old"));
            if (age > HealthThresholds.SampleMaxAge)
            {
                text.AppendLine(FormattableString.Invariant($"  WARN newest sample is older than {(long)HealthThresholds.SampleMaxAge.TotalSeconds} s - the runner is not recording"));
            }

            text.AppendLine($"  running   {N(sample.Docker.Running)} sandbox(es)   containers {N(sample.Docker.Containers)}");
            text.AppendLine($"  warm      busy {sample.Warm.Busy} / idle {sample.Warm.Idle} (total {sample.Warm.Total})");
            foreach (var (name, stream) in sample.Streams)
            {
                text.AppendLine($"  {name,-9} queued {N(stream.Length)}  pending {N(stream.Pending)}  lag {N(stream.Lag)}");
            }

            text.AppendLine($"  RAM       {N(host.MemAvailableMb)} MB free of {N(host.MemTotalMb)} MB ({Percent(host.MemAvailableMb, host.MemTotalMb)})  cache {N(host.CacheMb)} MB  anon {N(host.AnonMb)} MB");
            text.AppendLine($"  swap      {N(host.SwapUsedMb)} MB used of {N(host.SwapTotalMb)} MB");
            text.AppendLine($"  CPU       {D(host.CpuPercent)}%  load1 {D(host.Load1)}");
            text.AppendLine($"  disk      {D(host.DiskPercent)}% used");
            text.Append($"  shims     {N(sample.Docker.Shims)}  orphans {N(sample.Docker.OrphanShims)}");
            return text.ToString();
        }

        public static string Hour(IReadOnlyList<MetricsSample> samples)
        {
            var text = new StringBuilder();
            text.AppendLine("Last hour, per 5 minutes (min/avg/max)");
            text.AppendLine("  time   memFree MB        cpu %           running     queue lag   orphans");
            foreach (var group in samples.GroupBy(s => BucketStart(s.Ts)).OrderBy(g => g.Key))
            {
                var rows = group.ToList();
                text.AppendLine(string.Join("  ", new[]
                {
                    $"  {group.Key:HH:mm}",
                    Range(rows.Select(s => s.Host.MemAvailableMb), 16),
                    Range(rows.Select(s => s.Host.CpuPercent), 14),
                    Range(rows.Select(s => s.Docker.Running), 10),
                    Range(rows.Select(TotalLag), 10),
                    Range(rows.Select(s => s.Docker.OrphanShims), 6),
                }));
            }
            return text.ToString().TrimEnd();
        }

        public static string Events(IReadOnlyList<MetricsEvent> events)
        {
            if (events.Count == 0) return "no events";
            return string.Join('\n', events.Select(e =>
                $"{e.Ts:yyyy-MM-dd HH:mm:ss}Z  {e.Level,-7} {e.Kind,-14} {e.Message}"));
        }

        private static DateTimeOffset BucketStart(DateTimeOffset ts)
        {
            var ticks = ts.UtcTicks - (ts.UtcTicks % Bucket.Ticks);
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        private static long? TotalLag(MetricsSample sample)
        {
            var lags = sample.Streams.Values.Where(s => s.Lag is not null).Select(s => s.Lag!.Value).ToList();
            return lags.Count == 0 ? null : lags.Sum();
        }

        private static string Range(IEnumerable<double?> values, int width)
        {
            var known = values.Where(v => v is not null).Select(v => v!.Value).ToList();
            var cell = known.Count == 0
                ? "-"
                : string.Create(CultureInfo.InvariantCulture, $"{known.Min():0.#}/{known.Average():0.#}/{known.Max():0.#}");
            return cell.PadRight(width);
        }

        private static string Range(IEnumerable<long?> values, int width) =>
            Range(values.Select(v => (double?)v), width);

        private static string Range(IEnumerable<int?> values, int width) =>
            Range(values.Select(v => (double?)v), width);

        private static string N(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

        private static string D(double? value) => value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "?";

        private static string Percent(long? part, long? total) => part is { } p && total is > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{100.0 * p / total.Value:0}%")
            : "?";
    }
}
