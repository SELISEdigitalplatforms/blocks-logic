using System.Diagnostics;
using System.Text;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>What one sandbox execution produced.</summary>
    public sealed record SandboxResult
    {
        public required SandboxOutput Output { get; init; }
        public required int ExitCode { get; init; }
        public required bool OomKilled { get; init; }
        public required bool TimedOut { get; init; }
        public required long DurationMs { get; init; }

        /// <summary>
        /// The highest memory usage observed for the container's cgroup while it ran, sampled via
        /// Docker's live stats stream (see <see cref="DockerSandbox.CollectStatsAsync"/>). Null
        /// when no sample arrived — a run that finished before the first ~1s sample, or one whose
        /// stats stream failed to attach at all.
        /// </summary>
        public long? PeakMemoryBytes { get; init; }

        /// <summary>
        /// Total CPU time the sandbox consumed for its whole run, in milliseconds (a cumulative
        /// cgroup counter, not a percentage), rounded up to at least 1 ms whenever any was
        /// measured. Null when none was — see <see cref="SandboxStatsAccumulator.CpuUsageMs"/>
        /// for why null rather than zero carries that meaning. Expect it to sit far below
        /// <see cref="DurationMs"/> for an I/O-bound function, which spends its wall time parked
        /// on a socket burning no CPU at all.
        /// </summary>
        public long? CpuUsageMs { get; init; }

        /// <summary>Set when the sandbox could not be created or started at all.</summary>
        public string? HostFailure { get; init; }
    }

    /// <summary>Runs one function container to completion under the full security profile.</summary>
    public interface ISandbox
    {
        Task<SandboxResult> RunAsync(
            string runId,
            string image,
            string envelopeHostPath,
            RunLimits limits,
            CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="ISandbox"/>
    public sealed class DockerSandbox : ISandbox
    {
        private readonly IDockerClient _docker;
        private readonly RunnerOptions _options;
        private readonly ILogger<DockerSandbox> _logger;

        public DockerSandbox(IDockerClient docker, IOptions<RunnerOptions> options, ILogger<DockerSandbox> logger)
        {
            _docker = docker;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<SandboxResult> RunAsync(
            string runId,
            string image,
            string envelopeHostPath,
            RunLimits limits,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentNullException.ThrowIfNull(limits);

            var containerName = SandboxProfile.ContainerName(runId);
            var stopwatch = Stopwatch.StartNew();
            string? containerId = null;

            try
            {
                // A container with this name can only be an orphan from a previous attempt at
                // the same run — a runner that died mid-execution leaves its sandbox behind, and
                // the name would otherwise make every retry fail instantly with a conflict.
                // Reaching here means this runner holds the lease, so the orphan is ours to clear.
                await RemoveByNameAsync(containerName).ConfigureAwait(false);

                // --- create -----------------------------------------------------------
                var parameters = SandboxProfile.Create(containerName, image, envelopeHostPath, limits, _options);
                CreateContainerResponse created;
                try
                {
                    created = await _docker.Containers.CreateContainerAsync(parameters, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (DockerApiException ex)
                {
                    return HostFailure(stopwatch, $"the sandbox could not be created: {ex.Message}");
                }

                containerId = created.ID;

                // --- verify the profile before anything runs ---------------------------
                // The Engine is trusted, but a field it quietly ignored would weaken every
                // sandbox on this host. Check, then start.
                var inspectBefore = await _docker.Containers.InspectContainerAsync(containerId, cancellationToken)
                    .ConfigureAwait(false);
                var discrepancy = SandboxProfile.Validate(inspectBefore, limits, _options);
                if (discrepancy is not null)
                {
                    _logger.LogError(
                        "Refusing to start sandbox for run {RunId}: the security profile was not applied — {Discrepancy}",
                        runId, discrepancy);
                    return HostFailure(stopwatch, $"the security profile was not applied: {discrepancy}");
                }

                // --- attach before starting, so nothing printed is missed ---------------
                using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var stream = await _docker.Containers.AttachContainerAsync(
                    containerId,
                    tty: false,
                    new ContainerAttachParameters { Stream = true, Stdout = true, Stderr = true },
                    attachCts.Token).ConfigureAwait(false);

                var started = await _docker.Containers.StartContainerAsync(
                    containerId, new ContainerStartParameters(), cancellationToken).ConfigureAwait(false);
                if (!started)
                {
                    return HostFailure(stopwatch, "the sandbox did not start");
                }

                // Runs alongside the container for its whole life; cancelled once it stops (see
                // below), same shape as attachCts/readTask just below.
                using var statsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var statsTask = CollectStatsAsync(containerId, statsCts.Token);

                // --- run under a hard deadline -----------------------------------------
                // The bootstrap has its own soft deadline, but it races on the event loop and a
                // synchronous busy loop never yields to it. This is the deadline that actually
                // holds, which is why the grace is small and the kill is unconditional.
                var deadlineSeconds = limits.TimeoutSeconds + _options.KillGraceSeconds;
                var deadline = TimeSpan.FromSeconds(deadlineSeconds);
                using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadlineCts.CancelAfter(deadline);

                var readTask = ReadStreamAsync(stream, attachCts.Token);
                var timedOut = false;

                try
                {
                    await _docker.Containers.WaitContainerAsync(containerId, deadlineCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    timedOut = true;
                    _logger.LogWarning("Run {RunId} exceeded {Deadline}s — killing the sandbox", runId, deadlineSeconds);
                    await KillAsync(containerId).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Run {RunId} was cancelled — killing the sandbox", runId);
                    await KillAsync(containerId).ConfigureAwait(false);
                }

                stopwatch.Stop();

                // Give the attach a moment to drain, then stop waiting on it regardless: a
                // sandbox must never be able to hold the runner open.
                attachCts.CancelAfter(TimeSpan.FromSeconds(2));
                var stdout = await readTask.ConfigureAwait(false);

                // Docker closes the stats stream on its own once it sees the container stop, so
                // this normally returns immediately; the CancelAfter is only a backstop in case
                // that socket lingers.
                statsCts.CancelAfter(TimeSpan.FromSeconds(2));
                var (peakMemoryBytes, cpuUsageMs) = await statsTask.ConfigureAwait(false);

                // --- post-mortem --------------------------------------------------------
                var inspect = await _docker.Containers.InspectContainerAsync(containerId, CancellationToken.None)
                    .ConfigureAwait(false);

                var parser = new SandboxOutputParser();
                var output = parser.Parse(stdout);

                return new SandboxResult
                {
                    Output = output,
                    ExitCode = (int)(inspect.State?.ExitCode ?? -1),
                    OomKilled = inspect.State?.OOMKilled ?? false,
                    TimedOut = timedOut,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    PeakMemoryBytes = peakMemoryBytes,
                    CpuUsageMs = cpuUsageMs,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sandbox for run {RunId} failed on the host side", runId);
                return HostFailure(stopwatch, ex.Message);
            }
            finally
            {
                if (containerId is not null)
                {
                    await RemoveAsync(containerId).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Reads the multiplexed attach stream with a hard byte ceiling. stdout and stderr are
        /// interleaved deliberately: the bootstrap writes protocol lines to stdout, and anything
        /// on stderr is a package misbehaving, which is worth capturing next to it.
        /// </summary>
        private static async Task<string> ReadStreamAsync(MultiplexedStream stream, CancellationToken token)
        {
            // Twice the log ceiling plus the result ceiling: enough for a legitimate run, far
            // short of what a flooding sandbox would like to send.
            const long ceiling = (2 * Ceilings.LogBytes) + Ceilings.ResultBytes;

            var builder = new StringBuilder();
            var buffer = new byte[16 * 1024];
            long total = 0;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read.EOF) break;
                    if (read.Count == 0) continue;

                    total += read.Count;
                    if (total > ceiling)
                    {
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
                        break;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the deadline or the drain timer fires.
            }
            catch (IOException)
            {
                // The container went away mid-read; whatever arrived is what we have.
            }

            return builder.ToString();
        }

        /// <summary>
        /// Subscribes to the container's live stats stream for as long as it runs, folding the
        /// samples into peak memory and total CPU time via <see cref="SandboxStatsAccumulator"/>,
        /// which is where the reasoning about what those samples actually contain lives. Docker
        /// samples roughly once a second.
        /// <para>
        /// Best-effort and never fails the run: a container that finishes before the first sample
        /// arrives reports nothing (both come back null), and any failure attaching to or reading
        /// the stream is logged and swallowed here — this is enrichment for the run record, not
        /// something the sandbox's outcome should depend on.
        /// </para>
        /// </summary>
        private async Task<(long? PeakMemoryBytes, long? CpuUsageMs)> CollectStatsAsync(
            string containerId, CancellationToken cancellationToken)
        {
            var stats = new SandboxStatsAccumulator();
            var progress = new Progress<ContainerStatsResponse>(stats.Add);

            try
            {
                await _docker.Containers.GetContainerStatsAsync(
                    containerId,
                    new ContainerStatsParameters { Stream = true },
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the stream is cancelled once the container has finished running.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not collect resource stats for sandbox {ContainerId}", containerId);
            }

            return (stats.PeakMemoryBytes, stats.CpuUsageMs);
        }

        /// <summary>Removes a container by name, if one exists. Quiet when there is nothing there.</summary>
        private async Task RemoveByNameAsync(string containerName)
        {
            try
            {
                var existing = await _docker.Containers.ListContainersAsync(new ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [$"^/{containerName}$"] = true },
                    },
                }, CancellationToken.None).ConfigureAwait(false);

                foreach (var container in existing)
                {
                    _logger.LogWarning(
                        "Removing an orphaned sandbox {Name} ({Status}) left by an earlier attempt",
                        containerName, container.Status);
                    await RemoveAsync(container.ID).ConfigureAwait(false);
                }
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning("Could not check for an orphaned sandbox {Name}: {Message}", containerName, ex.Message);
            }
        }

        private async Task KillAsync(string containerId)
        {
            try
            {
                await _docker.Containers.KillContainerAsync(
                    containerId, new ContainerKillParameters { Signal = "SIGKILL" }, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (DockerApiException)
            {
                // Already gone.
            }
        }

        private async Task RemoveAsync(string containerId)
        {
            try
            {
                await _docker.Containers.RemoveContainerAsync(
                    containerId,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning("Could not remove sandbox {ContainerId}: {Message}", containerId, ex.Message);
            }
        }

        private static SandboxResult HostFailure(Stopwatch stopwatch, string message)
        {
            stopwatch.Stop();
            return new SandboxResult
            {
                Output = new SandboxOutput(),
                ExitCode = -1,
                OomKilled = false,
                TimedOut = false,
                DurationMs = stopwatch.ElapsedMilliseconds,
                HostFailure = message,
            };
        }
    }
}
