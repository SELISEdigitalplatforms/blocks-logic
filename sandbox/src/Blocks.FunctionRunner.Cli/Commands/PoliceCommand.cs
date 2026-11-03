using System.Diagnostics;
using System.Globalization;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Blocks.FunctionRunner.Cli.Commands
{
    /// <summary>
    /// <c>fnctl police [--fix]</c>: the check that does not trust the runner. It runs as root from
    /// a systemd timer, reads the host and Docker itself, and ends the one kind of leak the
    /// non-root runner cannot clean (orphaned shims). Rules live in <see cref="PoliceRules"/>;
    /// this class only reads the host, prints, and records WARN and FAIL.
    /// </summary>
    internal sealed class PoliceCommand
    {
        private const string ServiceName = "blocks-function-runner";

        private readonly MetricsStore _store;
        private readonly bool _fix;
        private readonly int _warmMaxAgeSeconds;
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public PoliceCommand(string dir, bool fix, int warmMaxAgeSeconds)
        {
            _store = new MetricsStore(dir, TimeProvider.System, createDirectory: false);
            _fix = fix;
            _warmMaxAgeSeconds = warmMaxAgeSeconds;
        }

        public async Task<int> RunAsync()
        {
            var results = new List<PoliceResult>();
            var state = await SystemctlAsync("is-active", ServiceName).ConfigureAwait(false);
            results.Add(PoliceRules.ServiceActive(state));
            results.Add(PoliceRules.SampleFresh(SafeLatest(), _now));
            results.AddRange(await DockerChecksAsync().ConfigureAwait(false));
            results.Add(PoliceRules.GcFresh(_store.ReadGcLast(), _now, await RunnerUptimeAsync().ConfigureAwait(false)));
            results.Add(PoliceRules.HostPressure(new HostProbe().Read()));

            foreach (var result in results)
            {
                Console.WriteLine($"  {Label(result.Level)} {result.Check,-9} {result.Message}");
                Record(result);
            }
            _store.PruneDaily(MetricsStore.PolicePrefix);

            var code = PoliceRules.ExitCode(results);
            Console.WriteLine(code == 0 ? "police: no failures" : "police: FAIL");
            return code;
        }

        private async Task<IReadOnlyList<PoliceResult>> DockerChecksAsync()
        {
            try
            {
                using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
                var results = new List<PoliceResult>();
                results.AddRange(await OrphanChecksAsync(docker).ConfigureAwait(false));
                var sandboxes = await ListSandboxesAsync(docker).ConfigureAwait(false);
                results.Add(PoliceRules.PausedTooLong(sandboxes, _now, _warmMaxAgeSeconds));
                results.Add(PoliceRules.RunningTooLong(sandboxes, _now));
                results.Add(PoliceRules.ArtifactImages(await CountUnlabelledArtifactImagesAsync(docker).ConfigureAwait(false)));
                return results;
            }
            catch (Exception ex) when (ex is DockerApiException or HttpRequestException or IOException or TimeoutException)
            {
                return [new PoliceResult("docker", PoliceLevel.Fail, $"docker unreachable: {ex.Message}")];
            }
        }

        private async Task<IReadOnlyList<PoliceResult>> OrphanChecksAsync(DockerClient docker)
        {
            // Shims first, then Docker's list, so a container created in between is never an orphan.
            var shims = ShimScanner.Scan();
            var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }).ConfigureAwait(false);
            var ids = containers.Select(c => c.ID).ToHashSet(StringComparer.Ordinal);

            var all = ShimScanner.Orphans(shims, ids, _now, TimeSpan.Zero);
            if (!_fix || all.Count == 0) return [PoliceRules.Orphans(all.Count)];

            var eligible = ShimScanner.Orphans(shims, ids, _now, HealthThresholds.PoliceOrphanMinAge);
            var (ended, failed) = await new ShimReaper(docker)
                .EndAsync(eligible, line => Console.WriteLine($"       {line}")).ConfigureAwait(false);
            return [PoliceRules.OrphansAfterFix(ended, failed, all.Count - eligible.Count)];
        }

        private async Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(DockerClient docker)
        {
            var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [SandboxProfile.SandboxLabel] = true },
                },
            }).ConfigureAwait(false);

            var limit = TimeSpan.FromSeconds(Contracts.Ceilings.HardLimitSeconds) + HealthThresholds.RunningExtraGrace;
            var result = new List<SandboxInfo>();
            foreach (var c in containers)
            {
                var created = new DateTimeOffset(DateTime.SpecifyKind(c.Created, DateTimeKind.Utc));
                var warm = c.Labels is not null && c.Labels.ContainsKey(SandboxProfile.WarmLabel);
                // The last start is only asked for when the container is old enough to matter.
                var started = c.State == "running" && !warm && _now - created > limit
                    ? await StartedAtAsync(docker, c.ID).ConfigureAwait(false)
                    : null;
                result.Add(new SandboxInfo(c.ID, c.State, warm, created, started));
            }
            return result;
        }

        private static async Task<DateTimeOffset?> StartedAtAsync(DockerClient docker, string id)
        {
            try
            {
                var inspect = await docker.Containers.InspectContainerAsync(id).ConfigureAwait(false);
                return DateTimeOffset.TryParse(inspect.State?.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            }
            catch (DockerContainerNotFoundException)
            {
                return null; // Gone between the list and the inspect: nothing to judge.
            }
        }

        private static async Task<int> CountUnlabelledArtifactImagesAsync(DockerClient docker)
        {
            var images = await docker.Images.ListImagesAsync(new ImagesListParameters { All = false }).ConfigureAwait(false);
            return images.Count(i =>
                i.RepoTags is not null
                && i.RepoTags.Any(t => t.StartsWith(ImageGc.ArtifactImagePrefix, StringComparison.Ordinal))
                && !(i.Labels is not null && i.Labels.TryGetValue(BuildProcessor.FunctionImageLabel, out var v) && v == "true"));
        }

        private MetricsSample? SafeLatest()
        {
            try
            {
                return _store.ReadLatestSample();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null; // Unreadable counts as "not recording", which the check reports.
            }
        }

        private void Record(PoliceResult result)
        {
            if (result.Level == PoliceLevel.Ok) return;
            _store.AppendPolice(new MetricsEvent(_now, $"police-{result.Check}", Label(result.Level).Trim(), result.Message));
        }

        private static string Label(PoliceLevel level) => level switch
        {
            PoliceLevel.Ok => "OK  ",
            PoliceLevel.Warn => "WARN",
            _ => "FAIL",
        };

        /// <summary>How long the runner process has been up; null when it is not running or cannot be read.</summary>
        private async Task<TimeSpan?> RunnerUptimeAsync()
        {
            var pidText = await SystemctlAsync("show", ServiceName, "-p", "MainPID", "--value").ConfigureAwait(false);
            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0) return null;
            try
            {
                var uptime = double.Parse((await File.ReadAllTextAsync("/proc/uptime").ConfigureAwait(false)).Split(' ')[0], CultureInfo.InvariantCulture);
                var started = ShimScanner.ParseStartTime(
                    await File.ReadAllTextAsync($"/proc/{pid}/stat").ConfigureAwait(false),
                    _now.AddSeconds(-uptime));
                return started is null ? null : _now - started.Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                return null; // The process ended while we looked.
            }
        }

        private static async Task<string> SystemctlAsync(params string[] args)
        {
            try
            {
                var start = new ProcessStartInfo("systemctl") { RedirectStandardOutput = true, UseShellExecute = false };
                foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start);
                if (process is null) return "unknown";
                var output = (await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Trim();
                await process.WaitForExitAsync().ConfigureAwait(false);
                return output.Length == 0 ? "unknown" : output;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "unknown"; // systemctl is not installed here.
            }
        }
    }
}
