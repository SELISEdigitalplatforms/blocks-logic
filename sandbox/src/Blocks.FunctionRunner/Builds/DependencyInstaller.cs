using System.Text;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>What one dependency install produced.</summary>
    public sealed record InstallResult
    {
        /// <summary>True only when npm succeeded and the archive is on disk.</summary>
        public required bool Ok { get; init; }

        /// <summary>Everything the install printed, stdout and stderr interleaved.</summary>
        public required string Log { get; init; }

        /// <summary>Set when the install failed; the reason a tenant is shown.</summary>
        public string? Failure { get; init; }
    }

    /// <summary>Installs a build's dependencies somewhere tenant code cannot reach the host.</summary>
    public interface IDependencyInstaller
    {
        /// <summary>
        /// Runs <c>npm install</c> for one build inside a gVisor sandbox and leaves
        /// <c>deps.tar</c> in <paramref name="workHostPath"/>.
        /// </summary>
        Task<InstallResult> InstallAsync(
            string buildId,
            string workHostPath,
            bool allowScripts,
            string beginMarker,
            string endMarker,
            CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IDependencyInstaller"/>
    public sealed class DependencyInstaller : IDependencyInstaller
    {
        /// <summary>
        /// Ceiling on what an install may print back. Generous — a large install is genuinely
        /// chatty — but finite, because the log is tenant-influenced and ends up in a build
        /// record. Reached, the read stops and the build keeps whatever arrived first.
        /// </summary>
        private const long LogCeilingBytes = 4L * 1024 * 1024;

        private readonly IDockerClient _docker;
        private readonly RunnerOptions _options;
        private readonly ILogger<DependencyInstaller> _logger;

        public DependencyInstaller(
            IDockerClient docker,
            IOptions<RunnerOptions> options,
            ILogger<DependencyInstaller> logger)
        {
            _docker = docker;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<InstallResult> InstallAsync(
            string buildId,
            string workHostPath,
            bool allowScripts,
            string beginMarker,
            string endMarker,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
            ArgumentException.ThrowIfNullOrWhiteSpace(workHostPath);

            var containerName = BuildSandboxProfile.ContainerName(buildId);
            var npmFlags = allowScripts ? "--omit=dev" : "--omit=dev --ignore-scripts";
            var script = BuildSandboxProfile.InstallScript(npmFlags, beginMarker, endMarker);
            var log = new StringBuilder();
            string? containerId = null;

            try
            {
                await RemoveOrphanAsync(containerName).ConfigureAwait(false);

                var parameters = BuildSandboxProfile.Create(
                    containerName, _options.BaseImage, workHostPath, script, _options);

                CreateContainerResponse created;
                try
                {
                    created = await _docker.Containers.CreateContainerAsync(parameters, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (DockerApiException ex)
                {
                    return Failed(log, $"the build sandbox could not be created: {ex.Message}");
                }

                containerId = created.ID;

                // --- verify the profile before anything runs ---------------------------
                var inspectBefore = await _docker.Containers.InspectContainerAsync(containerId, cancellationToken)
                    .ConfigureAwait(false);
                var discrepancy = BuildSandboxProfile.Validate(inspectBefore, _options);
                if (discrepancy is not null)
                {
                    _logger.LogError(
                        "Refusing to install dependencies for build {BuildId}: the security profile was " +
                        "not applied — {Discrepancy}", buildId, discrepancy);
                    return Failed(log, $"the build sandbox security profile was not applied: {discrepancy}");
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
                    return Failed(log, "the build sandbox did not start");
                }

                var readTask = ReadStreamAsync(stream, attachCts.Token);
                var timedOut = false;

                try
                {
                    await _docker.Containers.WaitContainerAsync(containerId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The caller's token carries the build timeout. A sandbox must never outlive
                    // it, so the kill is unconditional and happens before anything is reported.
                    timedOut = true;
                    _logger.LogWarning("Dependency install for build {BuildId} was cut short — killing the sandbox", buildId);
                    await KillAsync(containerId).ConfigureAwait(false);
                }

                // Give the attach a moment to drain, then stop waiting on it regardless: a build
                // sandbox must never be able to hold the runner open.
                attachCts.CancelAfter(TimeSpan.FromSeconds(2));
                log.Append(await readTask.ConfigureAwait(false));

                if (timedOut)
                {
                    return Failed(log, "the dependency install exceeded the build's time limit");
                }

                var inspect = await _docker.Containers.InspectContainerAsync(containerId, CancellationToken.None)
                    .ConfigureAwait(false);
                var exitCode = (int)(inspect.State?.ExitCode ?? -1);

                if (inspect.State?.OOMKilled == true)
                {
                    return Failed(log,
                        $"the dependency install ran out of memory at its {_options.BuildMemoryMb} MB ceiling");
                }

                if (exitCode != 0)
                {
                    // A DNS failure is the one npm error whose text points nowhere near its cause.
                    // Docker's embedded resolver is unreachable under gVisor, so every sandbox gets
                    // the host's resolv.conf bind-mounted over its own; when that file is missing or
                    // RUNNER__ResolvConf is wrong, every install in the fleet fails identically and
                    // npm reports it as the registry being unreachable. Naming it here is the
                    // difference between a one-line host fix and an afternoon spent on npm.
                    if (LooksLikeDnsFailure(log))
                    {
                        return Failed(log,
                            "the dependency install could not resolve the npm registry — this is a " +
                            $"host DNS problem, not a package one. Check that '{_options.ResolvConf}' " +
                            "exists and lists a reachable nameserver; it is bind-mounted over " +
                            "/etc/resolv.conf in every sandbox because Docker's embedded resolver " +
                            "does not work under gVisor.");
                    }

                    return Failed(log, $"the dependency install failed (npm exited {exitCode})");
                }

                // The script's last act is writing the archive, so its absence after a clean exit
                // means the sandbox died between the two — never treat that as an empty install.
                var archive = Path.Combine(workHostPath, BuildSandboxProfile.DepsArchiveName);
                if (!File.Exists(archive))
                {
                    return Failed(log, "the dependency install produced no archive");
                }

                _logger.LogInformation(
                    "Build {BuildId} installed dependencies in a {Runtime} sandbox ({Bytes} bytes)",
                    buildId, _options.Runtime, new FileInfo(archive).Length);

                return new InstallResult { Ok = true, Log = log.ToString() };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Dependency install for build {BuildId} failed on the host side", buildId);
                return Failed(log, ex.Message);
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
        /// Whether an install's output is a name-resolution failure rather than a package one.
        /// <para>
        /// Matched on npm's own codes, not on prose: <c>EAI_AGAIN</c> and <c>ENOTFOUND</c> are what
        /// a getaddrinfo failure surfaces as, and <c>getaddrinfo</c> appears in the underlying
        /// message. A registry that resolves but refuses (403, 404, ETARGET) is a real package
        /// problem and must not be reported as a host fault.
        /// </para>
        /// </summary>
        internal static bool LooksLikeDnsFailure(StringBuilder log)
        {
            var text = log.ToString();
            return text.Contains("EAI_AGAIN", StringComparison.Ordinal)
                || text.Contains("ENOTFOUND", StringComparison.Ordinal)
                || text.Contains("getaddrinfo", StringComparison.Ordinal);
        }

        private static InstallResult Failed(StringBuilder log, string failure) =>
            new() { Ok = false, Log = log.ToString(), Failure = failure };

        /// <summary>
        /// Reads the multiplexed attach stream with a hard byte ceiling, the same shape as a
        /// run's. stdout and stderr are interleaved deliberately: npm reports progress on one and
        /// warnings on the other, and a build log is only useful with both in order.
        /// </summary>
        private static async Task<string> ReadStreamAsync(MultiplexedStream stream, CancellationToken token)
        {
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

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));

                    total += read.Count;
                    if (total > LogCeilingBytes) break;
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
        /// Removes a container this build left behind on an earlier attempt. The name is
        /// deterministic, so a retry would otherwise collide with its own corpse.
        /// </summary>
        private async Task RemoveOrphanAsync(string containerName)
        {
            try
            {
                var existing = await _docker.Containers.ListContainersAsync(new ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [containerName] = true },
                    },
                }, CancellationToken.None).ConfigureAwait(false);

                foreach (var container in existing)
                {
                    if (container.Names?.Any(n => n.TrimStart('/') == containerName) != true) continue;

                    _logger.LogWarning(
                        "Removing an orphaned build sandbox {Name} ({Status}) left by an earlier attempt",
                        containerName, container.Status);
                    await RemoveAsync(container.ID).ConfigureAwait(false);
                }
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning(
                    "Could not check for an orphaned build sandbox {Name}: {Message}", containerName, ex.Message);
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
                _logger.LogWarning("Could not remove build sandbox {ContainerId}: {Message}", containerId, ex.Message);
            }
        }
    }
}
