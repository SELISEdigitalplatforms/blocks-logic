using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Maintenance
{
    /// <summary>
    /// Clears state that outlived the runner that created it — containers and run directories.
    /// <para>
    /// A runner killed mid-execution leaves its container running: the sandbox has no idea its
    /// supervisor is gone, and keeps burning CPU and memory until its own soft deadline. Nothing
    /// else will ever collect it, and its name blocks the retry of the very run it belongs to.
    /// </para>
    /// <para>
    /// A container is only reaped when no live lease exists for its run. That test is what makes
    /// this safe on a fleet: a sandbox another runner is legitimately supervising still has a
    /// lease, so it is left alone. Reaping runs once at startup and then periodically, because
    /// the crash that produced the orphan may have been on a different host.
    /// </para>
    /// <para>
    /// Run and build directories leak the same way, for the same reason: a SIGKILL skips the
    /// cleanup that would have removed them. They are safe to delete on the same no-live-lease
    /// test, because a retry rewrites its execution envelope from Redis before it starts.
    /// </para>
    /// </summary>
    public sealed class SandboxReaper : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        private readonly IDockerClient _docker;
        private readonly IDatabase _db;
        private readonly RunnerOptions _options;
        private readonly ILogger<SandboxReaper> _logger;

        public SandboxReaper(IDockerClient docker, IDatabase db, IOptions<RunnerOptions> options, ILogger<SandboxReaper> logger)
        {
            _docker = docker;
            _db = db;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReapAsync(stoppingToken).ConfigureAwait(false);
                    await ReapDirectoriesAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Sandbox reaping failed: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        internal async Task<int> ReapAsync(CancellationToken token)
        {
            var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"{SandboxProfile.SandboxLabel}=true"] = true },
                },
            }, token).ConfigureAwait(false);

            var reaped = 0;
            foreach (var container in containers)
            {
                var name = container.Names?.FirstOrDefault()?.TrimStart('/');
                if (name is null || !name.StartsWith(SandboxProfile.ContainerPrefix, StringComparison.Ordinal))
                    continue;

                var runId = name[SandboxProfile.ContainerPrefix.Length..];
                if (runId.Length == 0) continue;

                // A live lease means somebody is supervising this sandbox right now. Leave it.
                if (await _db.KeyExistsAsync(RedisKeys.Lease(runId)).ConfigureAwait(false)) continue;

                _logger.LogWarning(
                    "Reaping orphaned sandbox {Name} ({Status}); its run holds no lease, so no runner owns it",
                    name, container.Status);

                try
                {
                    await _docker.Containers.RemoveContainerAsync(
                        container.ID,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                        token).ConfigureAwait(false);
                    reaped++;
                }
                catch (DockerApiException ex)
                {
                    _logger.LogWarning("Could not reap {Name}: {Message}", name, ex.Message);
                }
            }

            if (reaped > 0) _logger.LogInformation("Reaped {Count} orphaned sandbox(es)", reaped);
            return reaped + await ReapBuildSandboxesAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears build sandboxes whose runner is gone.
        /// <para>
        /// These cannot use the lease test above, because a build takes no lease — there is no
        /// per-build key to ask about. Age stands in for it: a build sandbox is killed by its
        /// own runner at the build timeout, so one that has been alive for several times that
        /// long has no supervisor left. The multiple is what keeps this safe against a slow but
        /// legitimate install, which the owning runner is still holding a deadline over.
        /// </para>
        /// </summary>
        private async Task<int> ReapBuildSandboxesAsync(CancellationToken token)
        {
            var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"{BuildSandboxProfile.BuildSandboxLabel}=true"] = true },
                },
            }, token).ConfigureAwait(false);

            var abandoned = TimeSpan.FromSeconds(_options.BuildTimeoutSeconds * 3);
            var reaped = 0;

            foreach (var container in containers)
            {
                var name = container.Names?.FirstOrDefault()?.TrimStart('/');
                if (name is null || !name.StartsWith(BuildSandboxProfile.ContainerPrefix, StringComparison.Ordinal))
                    continue;

                var age = DateTime.UtcNow - container.Created.ToUniversalTime();
                if (age < abandoned) continue;

                _logger.LogWarning(
                    "Reaping abandoned build sandbox {Name} ({Status}); it has outlived {Age} and no runner owns it",
                    name, container.Status, abandoned);

                try
                {
                    await _docker.Containers.RemoveContainerAsync(
                        container.ID,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                        token).ConfigureAwait(false);
                    reaped++;
                }
                catch (DockerApiException ex)
                {
                    _logger.LogWarning("Could not reap {Name}: {Message}", name, ex.Message);
                }
            }

            if (reaped > 0) _logger.LogInformation("Reaped {Count} abandoned build sandbox(es)", reaped);
            return reaped;
        }

        /// <summary>
        /// Removes run and build directories whose work no runner owns. Only directories older
        /// than a grace period are considered, so a directory being written right now by another
        /// runner between creating it and taking its lease is never removed underneath it.
        /// </summary>
        internal async Task<int> ReapDirectoriesAsync()
        {
            var grace = TimeSpan.FromMinutes(2);
            var reaped = 0;

            foreach (var (root, leased) in new (string, bool)[] { (_options.RunsDir, true), (_options.BuildsDir, false) })
            {
                if (!Directory.Exists(root)) continue;

                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var id = Path.GetFileName(dir);
                    if (id.Length == 0) continue;

                    try
                    {
                        if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) < grace) continue;

                        if (leased && await _db.KeyExistsAsync(RedisKeys.Lease(id)).ConfigureAwait(false)) continue;

                        Directory.Delete(dir, recursive: true);
                        reaped++;
                        _logger.LogWarning("Reaped the orphaned directory {Dir}; no runner owns its work", dir);
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning("Could not reap {Dir}: {Message}", dir, ex.Message);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning("Could not reap {Dir}: {Message}", dir, ex.Message);
                    }
                }
            }

            return reaped;
        }
    }
}
