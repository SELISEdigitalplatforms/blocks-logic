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
    /// The auto clean-up (RUNNER-FLOW step 9): every 15 s, on its own timer, it lists the sandbox
    /// containers on this host from Docker and removes the ones that are dead, hung or nobody's — so
    /// the real containers clean themselves and nothing waits for a restart. Removed → its CPU and
    /// RAM are free at once. A Docker failure is logged and tried again on the next pass; it never
    /// blocks anything else.
    /// <list type="bullet">
    /// <item>A run container busy longer than the hard limit (90 s) → killed and removed, and its
    /// run's lease released so the run can be retried. So is one whose run holds no lease.</item>
    /// <item>A warm call (or start) busy longer than 90 s → its sandbox removed.</item>
    /// <item>A build container older than the hard build limit (15 min) → killed and removed.</item>
    /// <item>A warm sandbox this runner does not know (left by a crashed runner process) → removed;
    /// one of ours that Docker shows exited → removed from the pool. Paused ones are left to the
    /// warm clean-up.</item>
    /// </list>
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
        /// <summary>How often the clean-up runs (RUNNER-FLOW step 9.1).</summary>
        internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

        /// <summary>No run or warm call may be busy longer than this; past it the sandbox is dead or hung.</summary>
        internal static readonly TimeSpan RunHardLimit = TimeSpan.FromSeconds(Ceilings.HardLimitSeconds);

        private readonly IDockerClient _docker;
        private readonly IDatabase _db;
        private readonly RunnerOptions _options;
        private readonly ILogger<SandboxReaper> _logger;
        private readonly WarmPool? _warm;

        public SandboxReaper(
            IDockerClient docker, IDatabase db, IOptions<RunnerOptions> options, ILogger<SandboxReaper> logger,
            WarmPool? warm = null)
        {
            _docker = docker;
            _db = db;
            _options = options.Value;
            _logger = logger;
            _warm = warm;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReapAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Sandbox clean-up failed: {Message}; trying again in {Seconds} s", ex.Message, Interval.TotalSeconds);
                }

                try
                {
                    await ReapDirectoriesAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Directory clean-up failed: {Message}", ex.Message);
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

                // Busy past the hard limit: no run may last longer, so it is dead or hung — whoever
                // holds its lease. Its lease is released so the run can be retried.
                var hung = IsPastHardLimit(container.Created.ToUniversalTime(), DateTime.UtcNow);
                // A live lease means somebody is supervising this sandbox right now. Leave it.
                if (!hung && await _db.KeyExistsAsync(RedisKeys.Lease(runId)).ConfigureAwait(false)) continue;

                _logger.LogWarning("Removing run sandbox {Name} ({Status}): {Reason}", name, container.Status,
                    hung ? "busy longer than the hard limit, so dead or hung" : "its run holds no lease, so no runner owns it");

                if (!await RemoveAsync(container.ID, name, token).ConfigureAwait(false)) continue;
                reaped++;
                if (hung) await ReleaseLeaseAsync(runId).ConfigureAwait(false);
            }

            if (reaped > 0) _logger.LogInformation("Removed {Count} dead, hung or orphaned run sandbox(es)", reaped);
            var hungWarm = _warm is null ? 0 : await _warm.RemoveHungAsync(RunHardLimit).ConfigureAwait(false);
            return reaped + hungWarm
                + await ReapBuildSandboxesAsync(token).ConfigureAwait(false)
                + await ReapWarmSandboxesAsync(token).ConfigureAwait(false);
        }

        /// <summary>True when a run container created at <paramref name="createdUtc"/> has been busy longer than the hard limit.</summary>
        internal static bool IsPastHardLimit(DateTime createdUtc, DateTime nowUtc) => nowUtc - createdUtc > RunHardLimit;

        /// <summary>Kills and removes one container. A Docker failure is logged; the next pass tries again.</summary>
        private async Task<bool> RemoveAsync(string id, string name, CancellationToken token)
        {
            try
            {
                // Forced removal works on a running or paused container too: it is killed first.
                await _docker.Containers.RemoveContainerAsync(
                    id, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, token).ConfigureAwait(false);
                return true;
            }
            catch (DockerContainerNotFoundException)
            {
                return true;   // already gone
            }
            catch (DockerApiException ex)
            {
                _logger.LogError("Could not remove sandbox {Name}: {Message}; trying again next pass", name, ex.Message);
                return false;
            }
        }

        /// <summary>Lets another attempt take the run at once instead of waiting out its lease.</summary>
        private async Task ReleaseLeaseAsync(string runId)
        {
            try
            {
                await _db.KeyDeleteAsync(RedisKeys.Lease(runId)).ConfigureAwait(false);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning("Could not release the lease of run {RunId}: {Message}; it expires on its own", runId, ex.Message);
            }
        }

        /// <summary>
        /// Clears build sandboxes older than the hard build limit (15 min, RUNNER-FLOW step 9.3). A
        /// build takes no lease, so age stands in for it: its own runner kills it at the hard limit,
        /// so one alive past that has no supervisor left.
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

            var abandoned = TimeSpan.FromSeconds(_options.BuildTimeoutSeconds);
            var reaped = 0;

            foreach (var container in containers)
            {
                var name = container.Names?.FirstOrDefault()?.TrimStart('/');
                if (name is null || !name.StartsWith(BuildSandboxProfile.ContainerPrefix, StringComparison.Ordinal))
                    continue;

                var age = DateTime.UtcNow - container.Created.ToUniversalTime();
                if (age < abandoned) continue;

                _logger.LogWarning(
                    "Removing build sandbox {Name} ({Status}): it has outlived the {Age} build limit",
                    name, container.Status, abandoned);

                if (await RemoveAsync(container.ID, name, token).ConfigureAwait(false)) reaped++;
            }

            if (reaped > 0) _logger.LogInformation("Reaped {Count} abandoned build sandbox(es)", reaped);
            return reaped;
        }

        /// <summary>
        /// Clears warm (reusable) sandboxes nobody is keeping any more.
        /// <para>
        /// A warm sandbox belongs to no run and holds no lease, so neither test above applies. It
        /// is labelled at create time with the id of the runner that keeps it
        /// (<see cref="SandboxProfile.WarmLabel"/>). Ours, and not in our pool: the pool was lost
        /// with a previous process — a restart always starts with an empty pool — so the
        /// container is an orphan, paused or not. Another runner's: left alone until it is older
        /// than any runner keeps a warm sandbox (<see cref="RunnerOptions.WarmMaxAgeSeconds"/>)
        /// plus a margin, which only an orphan can be.
        /// </para>
        /// </summary>
        private async Task<int> ReapWarmSandboxesAsync(CancellationToken token)
        {
            var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [SandboxProfile.WarmLabel] = true },
                },
            }, token).ConfigureAwait(false);

            var reaped = 0;
            foreach (var container in containers)
            {
                var name = container.Names?.FirstOrDefault()?.TrimStart('/');
                if (name is null) continue;

                var owner = container.Labels is not null && container.Labels.TryGetValue(SandboxProfile.WarmLabel, out var o) ? o : null;

                // Ours, kept by the pool, and Docker says it is no longer running: dead. The pool
                // removes it (container included), so it stops holding a place under its cap.
                if (_warm is not null && IsExited(container.State) && _warm.IsTracked(name))
                {
                    if (await _warm.RemoveDeadAsync(name).ConfigureAwait(false)) reaped++;
                    continue;
                }

                if (!IsOrphanedWarm(name, owner, container.Created.ToUniversalTime(), DateTime.UtcNow, _options, _warm)) continue;

                _logger.LogWarning(
                    "Removing orphaned warm sandbox {Name} ({Status}) of runner '{Owner}'; no pool keeps it",
                    name, container.Status, owner);

                if (await RemoveAsync(container.ID, name, token).ConfigureAwait(false)) reaped++;
            }

            if (reaped > 0) _logger.LogInformation("Reaped {Count} orphaned warm sandbox(es)", reaped);
            return reaped;
        }

        /// <summary>Docker's state for a container that has stopped for good (not running, not paused).</summary>
        internal static bool IsExited(string? state) => state is "exited" or "dead";

        /// <summary>The decision <see cref="ReapWarmSandboxesAsync"/> makes about one container.</summary>
        internal static bool IsOrphanedWarm(
            string name, string? owner, DateTime createdUtc, DateTime nowUtc, RunnerOptions options, WarmPool? pool)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!name.StartsWith(SandboxProfile.WarmContainerPrefix, StringComparison.Ordinal)) return false;

            if (string.Equals(owner, options.RunnerId, StringComparison.Ordinal))
            {
                // A sandbox the pool is starting right now is already tracked (the pool records it
                // before the container is created), so "not tracked" really is "not ours any more".
                return pool?.IsTracked(name) != true;
            }

            var margin = TimeSpan.FromSeconds(options.WarmMaxAgeSeconds) + TimeSpan.FromMinutes(10);
            return nowUtc - createdUtc > margin;
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
