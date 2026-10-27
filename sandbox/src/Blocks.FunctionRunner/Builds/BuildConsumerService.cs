using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// Consumes deploy build jobs (RUNNER-FLOW step 1).
    /// <list type="bullet">
    /// <item>Up to <see cref="RunnerOptions.MaxParallelBuilds"/> (3) at once, counted from the build
    /// tasks running now.</item>
    /// <item>A build is read only when there is room: RAM, read live (the build's 2 GB above the
    /// reserve). RAM short → the oldest idle warm sandbox is removed first; still short → nothing is
    /// read, the build stays queued, and the check runs again within 5 s. Builds take no sandbox CPU:
    /// they run on the reserved cores at low priority.</item>
    /// <item>Newest wins: a build whose function has a newer deploy build queued is stopped at once.</item>
    /// <item>A build whose runner died is taken by another after the hard limit + 30 s; after
    /// <see cref="RunnerOptions.MaxAttempts"/> deliveries it is dead-lettered.</item>
    /// </list>
    /// </summary>
    public sealed class BuildConsumerService : BackgroundService
    {
        /// <summary>How often a running build checks whether a newer deploy replaced it.</summary>
        internal static readonly TimeSpan SupersededCheckInterval = TimeSpan.FromSeconds(2);

        /// <summary>The longest the loop waits before checking for room or work again (step 1.5: ≤ 5 s).</summary>
        private static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(5);

        private readonly IDatabase _db;
        private readonly BuildProcessor _processor;
        private readonly Admission.HostRoom _room;
        private readonly HeartbeatService _heartbeat;
        private readonly RunnerOptions _options;
        private readonly ILogger<BuildConsumerService> _logger;
        private readonly Sandbox.WarmPool? _warm;

        public BuildConsumerService(
            IDatabase db,
            BuildProcessor processor,
            Admission.HostRoom room,
            HeartbeatService heartbeat,
            IOptions<RunnerOptions> options,
            ILogger<BuildConsumerService> logger,
            Sandbox.WarmPool? warm = null)
        {
            _db = db;
            _processor = processor;
            _room = room;
            _heartbeat = heartbeat;
            _options = options.Value;
            _logger = logger;
            _warm = warm;
        }

        /// <summary>When another runner may take a build this one was given: the hard limit + 30 s (step 1.9).</summary>
        internal TimeSpan ReclaimAfter => TimeSpan.FromSeconds(_options.BuildTimeoutSeconds + 30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.ProcessBuilds)
            {
                _logger.LogInformation("Build processing is disabled on this host");
                return;
            }

            var consumer = new GroupConsumer(
                _db, _logger, RedisKeys.BuildsStream, RedisKeys.RunnerGroup, _options.RunnerId);
            await consumer.EnsureGroupAsync().ConfigureAwait(false);

            _logger.LogInformation("Consuming {Stream} as {Consumer}, up to {Max} builds at once",
                RedisKeys.BuildsStream, _options.RunnerId, _options.MaxParallelBuilds);

            var running = new List<Task>();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    running.RemoveAll(static t => t.IsCompleted);
                    if (_heartbeat.Latest is { Healthy: false }
                        || running.Count >= _options.MaxParallelBuilds
                        || !await HasRoomAsync().ConfigureAwait(false))
                    {
                        await Task.WhenAny([.. running, Task.Delay(RecheckDelay, stoppingToken)]).ConfigureAwait(false);
                        continue;
                    }

                    var entries = await consumer.ReadNewAsync(count: 1, stoppingToken).ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        entries = await consumer.ReclaimAbandonedAsync(ReclaimAfter, count: 1).ConfigureAwait(false);
                    }
                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    // Not the stop token for the build itself: a stopping host lets a started build
                    // finish within its own hard limit, as it does for runs.
                    running.AddRange(entries.Select(entry => HandleGuardedAsync(consumer, entry)));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The build loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }

            await Task.WhenAll(running).ConfigureAwait(false);
            _logger.LogInformation("Build loop stopped");
        }

        /// <summary>
        /// RAM for one more build, read live. Short → the oldest idle warm sandbox is removed and the
        /// check made once more (RUNNER-FLOW step 6.3): a paused sandbox nobody calls is worth less
        /// than a deploy that is waiting.
        /// </summary>
        private async Task<bool> HasRoomAsync()
        {
            if (_room.HasRoomForBuild()) return true;
            if (_warm is null || _room.BuildMemoryShortBytes() <= 0) return false;
            return await _warm.EvictIdleAsync().ConfigureAwait(false) && _room.HasRoomForBuild();
        }

        /// <summary>One build, off the loop. A failure stays with this build: logged, the entry left for a retry.</summary>
        private Task HandleGuardedAsync(GroupConsumer consumer, ClaimedEntry entry) =>
            Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(consumer, entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Build entry {EntryId} hit an unexpected error", entry.Id);
                }
            }, CancellationToken.None);

        private async Task HandleAsync(GroupConsumer consumer, ClaimedEntry entry)
        {
            var buildId = entry.Get("buildId");
            if (string.IsNullOrWhiteSpace(buildId))
            {
                await consumer.DeadLetterAsync(entry, "the entry carries no buildId").ConfigureAwait(false);
                return;
            }

            var deliveries = await consumer.DeliveryCountAsync(entry).ConfigureAwait(false);
            if (deliveries > _options.MaxAttempts)
            {
                await consumer.DeadLetterAsync(entry, $"delivered {deliveries} times without completing")
                    .ConfigureAwait(false);
                return;
            }

            var job = new BuildJob
            {
                BuildId = buildId,
                FunctionId = entry.Get("functionId") ?? buildId,
                TenantId = entry.Get("tenantId"),
                SourceKey = entry.Get("sourceKey") ?? RedisKeys.Source(buildId),
                ImageRef = entry.Get("imageRef") ?? string.Empty,
                ArtifactUploadUrl = entry.Get(RedisKeys.BuildArtifactUploadField),
                AllowScripts = string.Equals(entry.Get("allowScripts"), "true", StringComparison.OrdinalIgnoreCase),
            };

            if (string.IsNullOrWhiteSpace(job.ImageRef))
            {
                await consumer.DeadLetterAsync(entry, "the entry carries no imageRef").ConfigureAwait(false);
                return;
            }

            // Newest wins (step 1.1): stopped at once — before it starts, or while it runs.
            using var superseded = new CancellationTokenSource();
            using var watching = new CancellationTokenSource();
            if (await IsSupersededAsync(job).ConfigureAwait(false)) await superseded.CancelAsync().ConfigureAwait(false);
            var watch = WatchSupersededAsync(job, superseded, watching.Token);
            try
            {
                await _processor.ProcessAsync(job, CancellationToken.None, superseded.Token).ConfigureAwait(false);
            }
            finally
            {
                await watching.CancelAsync().ConfigureAwait(false);
                await watch.ConfigureAwait(false);
            }

            // The build result is already on its stream, so acknowledging now is safe.
            await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
        }

        /// <summary>True when the control plane has queued a newer deploy build of this build's function.</summary>
        internal async Task<bool> IsSupersededAsync(BuildJob job)
        {
            if (string.IsNullOrEmpty(job.TenantId)) return false;
            try
            {
                var latest = await _db.StringGetAsync(RedisKeys.BuildLatest(job.TenantId, job.FunctionId)).ConfigureAwait(false);
                // No key (an older control plane, or expired): nothing newer is known, the build goes on.
                return latest.HasValue && !string.Equals(latest.ToString(), job.BuildId, StringComparison.Ordinal);
            }
            catch (RedisException ex)
            {
                _logger.LogDebug("Could not check build {BuildId} for a newer deploy: {Message}", job.BuildId, ex.Message);
                return false;
            }
        }

        /// <summary>Checks every few seconds while the build runs; cancels it the moment a newer one is queued.</summary>
        private async Task WatchSupersededAsync(BuildJob job, CancellationTokenSource superseded, CancellationToken stop)
        {
            try
            {
                while (!superseded.IsCancellationRequested)
                {
                    await Task.Delay(SupersededCheckInterval, stop).ConfigureAwait(false);
                    if (!await IsSupersededAsync(job).ConfigureAwait(false)) continue;
                    _logger.LogInformation("Build {BuildId} of function {FunctionId} was replaced by a newer deploy; stopping it",
                        job.BuildId, job.FunctionId);
                    await superseded.CancelAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // The build ended.
            }
        }
    }
}
