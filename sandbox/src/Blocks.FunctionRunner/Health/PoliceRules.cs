using System.Globalization;

namespace Blocks.FunctionRunner.Health
{
    public enum PoliceLevel
    {
        Ok,
        Warn,
        Fail,
    }

    /// <summary>One line of <c>fnctl police</c>: which check, how it came out, and why.</summary>
    public sealed record PoliceResult(string Check, PoliceLevel Level, string Message);

    /// <summary>A sandbox container as police sees it. <c>Started</c> is the last start, when known.</summary>
    public sealed record SandboxInfo(string Id, string State, bool Warm, DateTimeOffset Created, DateTimeOffset? Started);

    /// <summary>
    /// The decisions behind <c>fnctl police</c>, as pure functions of what was read from the host.
    /// <para>
    /// Kept apart from the reading (systemd, Docker, /proc) so the rules can be tested without
    /// touching a live host: police runs as root every minute, and a test must never be able to
    /// end a real process or container.
    /// </para>
    /// </summary>
    public static class PoliceRules
    {
        /// <summary>0 when every check is OK or WARN, 1 when any FAIL.</summary>
        public static int ExitCode(IEnumerable<PoliceResult> results) =>
            results.Any(r => r.Level == PoliceLevel.Fail) ? 1 : 0;

        public static PoliceResult ServiceActive(string state) => state == "active"
            ? new PoliceResult("service", PoliceLevel.Ok, "runner service is active")
            : new PoliceResult("service", PoliceLevel.Fail, $"runner service is '{state}', not active");

        public static PoliceResult SampleFresh(MetricsSample? latest, DateTimeOffset now)
        {
            if (latest is null)
            {
                return new PoliceResult("recorder", PoliceLevel.Fail, "no metrics sample on disk - the runner is not recording");
            }
            var age = now - latest.Ts;
            return age <= HealthThresholds.SampleMaxAge
                ? new PoliceResult("recorder", PoliceLevel.Ok, $"newest sample is {Seconds(age)} s old")
                : new PoliceResult("recorder", PoliceLevel.Fail,
                    $"newest sample is {Seconds(age)} s old (limit {Seconds(HealthThresholds.SampleMaxAge)} s) - the runner is not recording");
        }

        /// <summary>
        /// Orphan shims found, not fixed: a leak, hence FAIL. <paramref name="all"/> is every shim
        /// Docker does not list.
        /// </summary>
        public static PoliceResult Orphans(int all) => all == 0
            ? new PoliceResult("shims", PoliceLevel.Ok, "no orphan containerd-shim processes")
            : new PoliceResult("shims", PoliceLevel.Fail, $"{all} orphan containerd-shim process(es) (run with --fix to end them)");

        /// <summary>
        /// After <c>--fix</c>. A shim that could not be ended is a FAIL; one still younger than
        /// <see cref="HealthThresholds.PoliceOrphanMinAge"/> is only a WARN, as it is left for the next run.
        /// </summary>
        public static PoliceResult OrphansAfterFix(int ended, int failed, int tooYoung)
        {
            var message = $"ended {ended} orphan shim(s); {failed} could not be ended; {tooYoung} younger than {Seconds(HealthThresholds.PoliceOrphanMinAge)} s left for the next run";
            return failed > 0
                ? new PoliceResult("shims", PoliceLevel.Fail, message)
                : new PoliceResult("shims", PoliceLevel.Warn, message);
        }

        public static PoliceResult PausedTooLong(IEnumerable<SandboxInfo> sandboxes, DateTimeOffset now, int warmMaxAgeSeconds)
        {
            var limit = TimeSpan.FromSeconds(warmMaxAgeSeconds) + HealthThresholds.PausedExtraGrace;
            var stuck = sandboxes.Where(s => s.Warm && s.State == "paused" && now - s.Created > limit).ToList();
            return stuck.Count == 0
                ? new PoliceResult("paused", PoliceLevel.Ok, "no paused warm sandbox is past its age limit")
                : new PoliceResult("paused", PoliceLevel.Fail,
                    $"{stuck.Count} paused warm sandbox(es) older than {Seconds(limit)} s: {Ids(stuck)}");
        }

        public static PoliceResult RunningTooLong(IEnumerable<SandboxInfo> sandboxes, DateTimeOffset now)
        {
            var limit = TimeSpan.FromSeconds(Contracts.Ceilings.HardLimitSeconds) + HealthThresholds.RunningExtraGrace;
            // Warm sandboxes are excluded: they live for up to an hour by design and a container's
            // start time does not change when it is unpaused for a call, so their age says nothing here.
            var stuck = sandboxes.Where(s => !s.Warm && s.State == "running" && now - (s.Started ?? s.Created) > limit).ToList();
            return stuck.Count == 0
                ? new PoliceResult("running", PoliceLevel.Ok, "no sandbox runs past the hard limit")
                : new PoliceResult("running", PoliceLevel.Fail,
                    $"{stuck.Count} sandbox(es) running longer than {Seconds(limit)} s: {Ids(stuck)}");
        }

        public static PoliceResult ArtifactImages(int unlabelled) => unlabelled == 0
            ? new PoliceResult("images", PoliceLevel.Ok, "every blocks-fn-artifact image carries the function label")
            : new PoliceResult("images", PoliceLevel.Warn,
                $"{unlabelled} blocks-fn-artifact image(s) lack the function label (image GC finds them by name)");

        /// <param name="runnerUptime">
        /// How long the runner has been up, when known. The first sweep runs 10 minutes after a
        /// start, so a young runner without <c>gc-last.json</c> is not yet a failure.
        /// </param>
        public static PoliceResult GcFresh(GcLast? gc, DateTimeOffset now, TimeSpan? runnerUptime)
        {
            if (gc is not null && now - gc.Ts <= HealthThresholds.GcMaxAge)
            {
                return new PoliceResult("gc", PoliceLevel.Ok, $"last image sweep {Seconds(now - gc.Ts)} s ago, removed {gc.Removed}");
            }
            if (runnerUptime is { } up && up <= HealthThresholds.GcMaxAge)
            {
                return new PoliceResult("gc", PoliceLevel.Ok, "runner started recently; the first image sweep is not due yet");
            }
            return new PoliceResult("gc", PoliceLevel.Fail, gc is null
                ? "no image sweep recorded (gc-last.json missing)"
                : $"last image sweep was {Seconds(now - gc.Ts)} s ago (limit {Seconds(HealthThresholds.GcMaxAge)} s)");
        }

        /// <summary>Memory, swap and disk; CPU is a rate and is left to the recorder.</summary>
        public static PoliceResult HostPressure(HostStats host)
        {
            var breaches = HealthRules.HostBreaches(host with { CpuPercent = null }).ToList();
            return breaches.Count == 0
                ? new PoliceResult("host", PoliceLevel.Ok, "memory, swap and disk are within limits")
                : new PoliceResult("host", PoliceLevel.Warn, string.Join("; ", breaches.Select(b => b.Message)));
        }

        private static string Ids(IEnumerable<SandboxInfo> sandboxes) =>
            string.Join(", ", sandboxes.Take(5).Select(s => s.Id[..Math.Min(12, s.Id.Length)]));

        private static string Seconds(TimeSpan span) => ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);
    }
}
