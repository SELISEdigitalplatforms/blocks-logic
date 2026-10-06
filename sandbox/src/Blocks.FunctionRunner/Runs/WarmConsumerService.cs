using System.Globalization;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Sandbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Looks after the warm pool: pre-warm and drain requests from <see cref="RedisKeys.WarmStream"/>,
    /// and the periodic sweep that retires idle sandboxes. Does nothing at all unless
    /// <see cref="RunnerOptions.SandboxReuse"/> is on.
    /// <para>
    /// Unlike the runs and builds streams, every runner reads every entry here: each through a
    /// consumer group of its own (<see cref="RedisKeys.WarmGroup"/>). A pre-warm is a hint any
    /// host may act on, but a drain is an instruction each host holding the old version must
    /// see — with one shared group, only whichever runner claimed it would have drained. Entries
    /// are therefore acknowledged and never deleted here (another runner's group still owes
    /// them); the control plane keeps the stream short with MAXLEN when it adds to it.
    /// </para>
    /// <para>
    /// Everything here is best effort. A pre-warm that finds no room starts fewer sandboxes, or
    /// none; nothing is retried, because the first real run starts one anyway.
    /// </para>
    /// </summary>
    public sealed class WarmConsumerService : BackgroundService
    {
        /// <summary>How often idle sandboxes are checked against the idle timeout and the maximum age.</summary>
        internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

        /// <summary>The most sandboxes one entry may ask for; a typo must not fill the host.</summary>
        internal const int MaxCountPerEntry = 50;

        private readonly IDatabase _db;
        private readonly WarmPool _pool;
        private readonly IImageResolver _images;
        private readonly RunnerOptions _options;
        private readonly ILogger<WarmConsumerService> _logger;

        public WarmConsumerService(
            IDatabase db,
            WarmPool pool,
            IImageResolver images,
            IOptions<RunnerOptions> options,
            ILogger<WarmConsumerService> logger)
        {
            _db = db;
            _pool = pool;
            _images = images;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.SandboxReuse || !_options.ProcessRuns)
            {
                _logger.LogInformation("Sandbox reuse is off on this host; not pre-warming");
                return;
            }

            var group = RedisKeys.WarmGroup(_options.RunnerId);
            var consumer = new GroupConsumer(_db, _logger, RedisKeys.WarmStream, group, _options.RunnerId);
            await EnsureGroupAsync(group).ConfigureAwait(false);

            _logger.LogInformation("Consuming {Stream} as {Group}", RedisKeys.WarmStream, group);

            var nextSweep = DateTimeOffset.UtcNow.Add(SweepInterval);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (DateTimeOffset.UtcNow >= nextSweep)
                    {
                        nextSweep = DateTimeOffset.UtcNow.Add(SweepInterval);
                        await _pool.SweepAsync().ConfigureAwait(false);
                    }

                    var entries = await consumer.ReadNewAsync(count: 10, stoppingToken).ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await HandleThenAcknowledgeAsync(
                            () => HandleAsync(entry, stoppingToken),
                            () => _db.StreamAcknowledgeAsync(RedisKeys.WarmStream, group, entry.Id),
                            _logger).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "The warm loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Handles one entry and acknowledges it whatever happened. A pre-warm is a best-effort
        /// hint: one that failed is logged and dropped, never retried — retrying would only grow
        /// this group's pending list with entries nobody acknowledges, and the version's first run
        /// starts a sandbox anyway.
        /// </summary>
        internal static async Task HandleThenAcknowledgeAsync(Func<Task> handle, Func<Task> acknowledge, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(handle);
            ArgumentNullException.ThrowIfNull(acknowledge);
            try
            {
                await handle().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "A pre-warm request failed; dropping it");
            }
            finally
            {
                await acknowledge().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// This runner's group, created at the stream's end: a new or restarted runner acts on
        /// requests made from now on, not on every deploy since the stream began.
        /// </summary>
        private async Task EnsureGroupAsync(string group)
        {
            try
            {
                await _db.StreamCreateConsumerGroupAsync(RedisKeys.WarmStream, group, StreamPosition.NewMessages, createStream: true)
                    .ConfigureAwait(false);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // Already there, from this runner's previous life.
            }
        }

        /// <summary>One pre-warm entry: drain the old version first (it frees room), then warm the new.</summary>
        internal async Task HandleAsync(ClaimedEntry entry, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var tenantId = entry.Get("tenantId") ?? string.Empty;
            var functionId = entry.Get("functionId");
            var versionId = entry.Get("versionId");
            var image = entry.Get("image");
            var drain = entry.Get("drainVersionId");

            if (!string.IsNullOrWhiteSpace(functionId) && !string.IsNullOrWhiteSpace(drain))
            {
                var drained = await _pool.DrainAsync(tenantId, functionId, drain).ConfigureAwait(false);
                if (drained > 0)
                {
                    _logger.LogInformation(
                        "Drained {Count} idle warm sandbox(es) of function {FunctionId} version {VersionId}",
                        drained, functionId, drain);
                }
            }

            // Per runner, deliberately: every runner reads every entry (its own group), so a count
            // of 2 on a fleet of three warms up to six sandboxes — each host that may receive the
            // version's runs gets its own, and each stays within its own host budget.
            var count = Math.Clamp(entry.GetInt("count", 1), 0, MaxCountPerEntry);
            if (count == 0 || string.IsNullOrWhiteSpace(functionId) || string.IsNullOrWhiteSpace(versionId)
                || string.IsNullOrWhiteSpace(image))
            {
                return;
            }

            // The run entry's image reference, resolved the same way a run resolves it. A
            // pre-warm carries no artifact URL, so a host that cannot get the image from its
            // registry simply does not pre-warm it; its first run builds it as usual.
            var resolved = await _images.EnsureAsync(image, token).ConfigureAwait(false);
            if (resolved is null)
            {
                _logger.LogInformation("Not pre-warming function {FunctionId}: image {Image} is not available here",
                    functionId, image);
                return;
            }

            var limits = RunLimits.Clamp(
                ReadInt(entry, "cpuMillicores"), ReadLong(entry, "memoryBytes"), ReadInt(entry, "pidLimit"),
                ReadLong(entry, "tmpfsBytes"), ReadInt(entry, "timeoutSeconds"), ReadInt(entry, "concurrency"));

            var started = await _pool.PrewarmAsync(
                new WarmKey(tenantId, functionId, versionId, resolved), limits, count, token).ConfigureAwait(false);
            _logger.LogInformation(
                "Pre-warmed {Started} of {Count} sandbox(es) for function {FunctionId} version {VersionId}",
                started, count, functionId, versionId);
        }

        private static int? ReadInt(ClaimedEntry e, string key) =>
            int.TryParse(e.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        private static long? ReadLong(ClaimedEntry e, string key) =>
            long.TryParse(e.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
