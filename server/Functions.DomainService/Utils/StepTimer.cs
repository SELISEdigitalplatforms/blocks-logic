using System.Diagnostics;
using System.Text;
using Functions.DomainService.Models;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// Names where a request's time went, step by step, for one log line — so the slow step of a
    /// function call is measured rather than guessed (2026-10-06: ~1.3 s of a warm call was spent
    /// outside the runner, in the Api and the Worker). Times only, never values.
    /// </summary>
    public sealed class StepTimer
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly List<(string Step, long Ms)> _steps = [];
        private readonly Lock _gate = new();
        private long _last;

        /// <summary>
        /// The timer of the HTTP call this code runs under, if any. Set by the outermost step, so
        /// the inner ones can mark without every signature carrying it.
        /// </summary>
        public static readonly AsyncLocal<StepTimer?> Current = new();

        public long ElapsedMs => _watch.ElapsedMilliseconds;

        /// <summary>Records <paramref name="step"/> as the time since the previous mark.</summary>
        public void Mark(string step)
        {
            lock (_gate)
            {
                var now = _watch.ElapsedMilliseconds;
                _steps.Add((step, now - _last));
                _last = now;
            }
        }

        /// <summary><c>function=35;version=34;…</c> — the wire form a run entry carries.</summary>
        public string ToCompact()
        {
            lock (_gate)
            {
                return string.Join(';', _steps.Select(s => $"{s.Step}={s.Ms}"));
            }
        }

        /// <summary>
        /// Reads <c>group.step=ms;…</c> (or <c>step=ms</c>, grouped as itself) into timings. Anything
        /// malformed is skipped: the field crosses a queue and is not trusted.
        /// </summary>
        public static List<RunTiming> ParseCompact(string? compact)
        {
            var timings = new List<RunTiming>();
            if (string.IsNullOrWhiteSpace(compact)) return timings;
            foreach (var part in compact.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0 || eq > 60 || !long.TryParse(part.AsSpan(eq + 1), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var ms)) continue;
                var name = part[..eq];
                var dot = name.IndexOf('.');
                timings.Add(dot > 0
                    ? new RunTiming { Group = name[..dot], Step = name[(dot + 1)..], Ms = ms }
                    : new RunTiming { Group = name, Step = name, Ms = ms });
                if (timings.Count >= 40) break;
            }
            return timings;
        }

        /// <summary>The steps marked so far, under <paramref name="group"/>.</summary>
        public List<RunTiming> ToTimings(string group)
        {
            lock (_gate)
            {
                return [.. _steps.Select(s => new RunTiming { Group = group, Step = s.Step, Ms = s.Ms })];
            }
        }

        /// <summary><c>function 35 ms, version 34 ms, …</c></summary>
        public override string ToString()
        {
            lock (_gate)
            {
                var text = new StringBuilder();
                foreach (var (step, ms) in _steps)
                {
                    if (text.Length > 0) text.Append(", ");
                    text.Append(step).Append(' ').Append(ms).Append(" ms");
                }
                return text.ToString();
            }
        }
    }
}
