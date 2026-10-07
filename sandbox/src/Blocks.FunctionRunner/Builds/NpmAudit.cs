using System.Text.Json;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// The advisory check every dependency install runs (F-8): <c>npm audit --omit=dev</c> inside the
    /// same build sandbox, read back from a nonce-fenced block in the install log.
    /// <para>
    /// Policy (<c>RUNNER__AuditFailLevel</c>, default <c>critical</c>): an advisory at or above the
    /// level fails the build and names the packages; anything below is listed in the build log as a
    /// warning. <c>none</c> never fails. If the check cannot run — the advisory service unreachable,
    /// output unreadable — the build is not failed: an outage of npm's audit endpoint must not stop
    /// every build on the platform. It is said in the log instead.
    /// </para>
    /// </summary>
    public static class NpmAudit
    {
        /// <summary>Severities in npm's order, lowest first.</summary>
        public static readonly string[] Levels = ["info", "low", "moderate", "high", "critical"];

        /// <summary>The fences for the audit block, derived from the build's packages fences.</summary>
        public static string BeginMarker(string packagesBegin) => packagesBegin + "-audit";
        public static string EndMarker(string packagesEnd) => packagesEnd + "-audit";

        /// <summary>What one audit found: package names by severity.</summary>
        public sealed record Report(IReadOnlyDictionary<string, IReadOnlyList<string>> BySeverity)
        {
            public int Count(string level) => BySeverity.TryGetValue(level, out var names) ? names.Count : 0;
        }

        /// <summary>
        /// The report from the last fenced block, or null when the check did not produce one (no
        /// block, an error object, or JSON that is not an audit report).
        /// </summary>
        public static Report? Parse(string log, string begin, string end)
        {
            var block = BuildProcessor.ExtractBlock(log, begin, end);
            if (string.IsNullOrWhiteSpace(block)) return null;

            try
            {
                using var doc = JsonDocument.Parse(block);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)) return null;
                if (!root.TryGetProperty("vulnerabilities", out var vulns) || vulns.ValueKind != JsonValueKind.Object) return null;

                var bySeverity = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var vuln in vulns.EnumerateObject())
                {
                    if (vuln.Value.ValueKind != JsonValueKind.Object
                        || !vuln.Value.TryGetProperty("severity", out var severity)
                        || severity.ValueKind != JsonValueKind.String) continue;

                    var level = severity.GetString()!;
                    if (!bySeverity.TryGetValue(level, out var names)) bySeverity[level] = names = [];
                    names.Add(vuln.Name);
                }

                return new Report(bySeverity.ToDictionary(
                    kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Order(StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>True for a level this policy understands; <c>none</c> turns failing off.</summary>
        public static bool IsValidLevel(string? level) =>
            level is not null && (level == "none" || Levels.Contains(level, StringComparer.Ordinal));

        /// <summary>The build-failing reason under <paramref name="failLevel"/>, or null to pass.</summary>
        public static string? Failure(Report? report, string failLevel)
        {
            if (report is null || failLevel == "none") return null;

            var threshold = Array.IndexOf(Levels, failLevel);
            if (threshold < 0) return null;

            var blocking = Levels.Skip(threshold)
                .Where(level => report.Count(level) > 0)
                .Select(level => $"{level}: {string.Join(", ", report.BySeverity[level].Take(10))}")
                .ToList();

            return blocking.Count == 0
                ? null
                : $"known security advisories at '{failLevel}' or above in the dependencies ({string.Join("; ", blocking)}). " +
                  "Update those packages, or pin a fixed version, and build again";
        }

        /// <summary>One line for the build log: what was found, or that the check could not run.</summary>
        public static string Summary(Report? report)
        {
            if (report is null)
                return "advisory check: could not run (npm audit gave no report); the build is not blocked by it";

            var found = Levels.Reverse()
                .Where(level => report.Count(level) > 0)
                .Select(level => $"{report.Count(level)} {level} ({string.Join(", ", report.BySeverity[level].Take(5))})")
                .ToList();

            return found.Count == 0
                ? "advisory check: no known advisories"
                : $"advisory check: {string.Join("; ", found)}";
        }
    }
}
