using System.Globalization;
using System.Text.Json;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// The files under <c>MetricsDir</c>: what is written, what is kept, and how it is read back.
    /// <para>
    /// Plain files and plain JSON lines, no database, so an operator can <c>tail</c> or <c>jq</c>
    /// them on a host whose runner is already in trouble. It takes a clock and a directory and
    /// nothing else, so retention can be tested without the host.
    /// </para>
    /// <para>
    /// Writing must never break the runner: every IO failure is swallowed here and reported once
    /// through <see cref="WriteFailed"/>. (It is an event rather than a logger on purpose: the
    /// error-log provider writes through this class, and a logger inside it would loop.)
    /// </para>
    /// </summary>
    public sealed class MetricsStore
    {
        public const string SamplesPrefix = "samples";
        public const string EventsPrefix = "events";
        public const string PolicePrefix = "police";
        public const string GcLastFile = "gc-last.json";

        private const string WindowFormat = "yyyyMMddHHmm";
        private const string DayFormat = "yyyyMMdd";

        private readonly string _dir;
        private readonly TimeProvider _clock;
        private readonly bool _createDirectory;
        private readonly object _gate = new();

        /// <param name="createDirectory">
        /// False for the root-side police: a directory root creates would belong to root, and the
        /// runner (a different user) could then not write its own samples into it.
        /// </param>
        public MetricsStore(string dir, TimeProvider clock, bool createDirectory = true)
        {
            _dir = dir;
            _clock = clock;
            _createDirectory = createDirectory;
        }

        /// <summary>Raised with a short reason when a write or a delete failed.</summary>
        public event Action<string>? WriteFailed;

        public string DirectoryPath => _dir;

        // ---------------------------------------------------------------- writing ----

        public void AppendSample(MetricsSample sample) =>
            Append(SampleFileName(sample.Ts), JsonSerializer.Serialize(sample, MetricsJson.Options));

        public void AppendEvent(MetricsEvent evt) => AppendDaily(EventsPrefix, evt);

        public void AppendPolice(MetricsEvent evt) => AppendDaily(PolicePrefix, evt);

        public void WriteGcLast(int removed)
        {
            var json = JsonSerializer.Serialize(new GcLast(_clock.GetUtcNow(), removed), MetricsJson.Options);
            Guarded(() =>
            {
                EnsureDirectory();
                // Written beside and renamed over, so police never reads half a file.
                var target = Path.Combine(_dir, GcLastFile);
                var temp = target + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, target, overwrite: true);
            });
        }

        /// <summary>Deletes sample files whose 10-minute window ended more than 60 minutes ago.</summary>
        public void PruneSamples()
        {
            var cutoff = _clock.GetUtcNow() - HealthThresholds.SampleRetention;
            PruneFiles(SamplesPrefix, start => start + HealthThresholds.SampleFileWindow < cutoff, WindowFormat);
        }

        /// <summary>Deletes <c>&lt;prefix&gt;-*.jsonl</c> day files whose day ended more than 7 days ago.</summary>
        public void PruneDaily(string prefix)
        {
            var cutoff = _clock.GetUtcNow() - TimeSpan.FromDays(HealthThresholds.EventRetentionDays);
            PruneFiles(prefix, day => day.AddDays(1) < cutoff, DayFormat);
        }

        // ---------------------------------------------------------------- reading ----

        /// <summary>The newest sample on disk, or null when there is none.</summary>
        public MetricsSample? ReadLatestSample()
        {
            foreach (var file in FilesNewestFirst(SamplesPrefix))
            {
                var last = ReadLines<MetricsSample>(file).LastOrDefault();
                if (last is not null) return last;
            }
            return null;
        }

        /// <summary>Samples from the last <paramref name="window"/>, oldest first.</summary>
        public IReadOnlyList<MetricsSample> ReadSamples(TimeSpan window)
        {
            var from = _clock.GetUtcNow() - window;
            return FilesNewestFirst(SamplesPrefix)
                .Where(f => FileStart(f, WindowFormat) + HealthThresholds.SampleFileWindow >= from)
                .SelectMany(ReadLines<MetricsSample>)
                .Where(s => s.Ts >= from)
                .OrderBy(s => s.Ts)
                .ToList();
        }

        /// <summary>Events (or police lines) of the last <paramref name="days"/> days, newest first.</summary>
        public IReadOnlyList<MetricsEvent> ReadEvents(int days, string prefix = EventsPrefix)
        {
            var from = _clock.GetUtcNow() - TimeSpan.FromDays(days);
            return FilesNewestFirst(prefix)
                .Where(f => FileStart(f, DayFormat).AddDays(1) >= from)
                .SelectMany(ReadLines<MetricsEvent>)
                .Where(e => e.Ts >= from)
                .OrderByDescending(e => e.Ts)
                .ToList();
        }

        /// <summary>The last image sweep, or null when none was recorded (or the file is unreadable).</summary>
        public GcLast? ReadGcLast()
        {
            var path = Path.Combine(_dir, GcLastFile);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<GcLast>(File.ReadAllText(path), MetricsJson.Options);
            }
            catch (JsonException)
            {
                return null; // A torn or foreign file counts as "no sweep recorded".
            }
        }

        // ---------------------------------------------------------------- names ------

        public static string SampleFileName(DateTimeOffset ts)
        {
            var utc = ts.UtcDateTime;
            var floored = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 10), 0, DateTimeKind.Utc);
            return $"{SamplesPrefix}-{floored.ToString(WindowFormat, CultureInfo.InvariantCulture)}.jsonl";
        }

        public static string DailyFileName(string prefix, DateTimeOffset ts) =>
            $"{prefix}-{ts.UtcDateTime.ToString(DayFormat, CultureInfo.InvariantCulture)}.jsonl";

        // ---------------------------------------------------------------- internals --

        private void AppendDaily(string prefix, MetricsEvent evt) =>
            Append(DailyFileName(prefix, evt.Ts), JsonSerializer.Serialize(evt, MetricsJson.Options));

        private void Append(string fileName, string line) => Guarded(() =>
        {
            EnsureDirectory();
            lock (_gate)
            {
                File.AppendAllText(Path.Combine(_dir, fileName), line + "\n");
            }
        });

        private void EnsureDirectory()
        {
            if (_createDirectory) System.IO.Directory.CreateDirectory(_dir);
        }

        private void Guarded(Action write)
        {
            try
            {
                write();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteFailed?.Invoke($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private void PruneFiles(string prefix, Func<DateTime, bool> isExpired, string format) => Guarded(() =>
        {
            foreach (var file in FilesNewestFirst(prefix))
            {
                if (isExpired(FileStart(file, format))) File.Delete(file);
            }
        });

        private List<string> FilesNewestFirst(string prefix)
        {
            if (!System.IO.Directory.Exists(_dir)) return [];
            return System.IO.Directory.EnumerateFiles(_dir, $"{prefix}-*.jsonl")
                .Where(f => TryFileStart(f, prefix, out _))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();
        }

        private static DateTime FileStart(string file, string format) =>
            DateTime.ParseExact(FileStamp(file), format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        private static bool TryFileStart(string file, string prefix, out DateTime start)
        {
            var format = prefix == SamplesPrefix ? WindowFormat : DayFormat;
            return DateTime.TryParseExact(FileStamp(file), format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out start);
        }

        private static string FileStamp(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            return name[(name.IndexOf('-', StringComparison.Ordinal) + 1)..];
        }

        private static List<T> ReadLines<T>(string file) where T : class
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var result = new List<T>();
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<T>(line, MetricsJson.Options);
                    if (item is not null) result.Add(item);
                }
                catch (JsonException)
                {
                    // The last line may be half written while the runner appends; skip it.
                }
            }
            return result;
        }
    }
}
