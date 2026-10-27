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
    /// Drains old versions from the warm pool: when a new version goes Live the control plane puts
    /// "drain the old version" on <see cref="RedisKeys.WarmStream"/> (RUNNER-FLOW step 2.2). Nothing
    /// is pre-warmed: a count or image on an entry from an older control plane is ignored, and the
    /// first call of a version warms it. The idle clean-up runs on its own timer
    /// (<see cref="WarmSweepService"/>), so nothing here can hold it up. Does nothing at all unless
    /// <see cref="RunnerOptions.SandboxReuse"/> is on.
    /// <para>
    /// Unlike the runs and builds streams, every runner reads every entry here: each through a
    /// consumer group of its own (<see cref="RedisKeys.WarmGroup"/>). A drain is an instruction each
    /// host holding the old version must see — with one shared group, only whichever runner claimed
    /// it would have drained. Entries
    /// are therefore acknowledged and never deleted here (another runner's group still owes
    /// them); the control plane keeps the stream short with MAXLEN when it adds to it.
    /// </para>
    /// </summary>
    public sealed class WarmConsumerService : BackgroundService
    {
        private readonly IDatabase _db;
        private readonly WarmPool _pool;
        private readonly RunnerOptions _options;
        private readonly ILogger<WarmConsumerService> _logger;

        public WarmConsumerService(
            IDatabase db,
            WarmPool pool,
            IOptions<RunnerOptions> options,
            ILogger<WarmConsumerService> logger)
        {
            _db = db;
            _pool = pool;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.SandboxReuse || !_options.ProcessRuns)
            {
                _logger.LogInformation("Sandbox reuse is off on this host; no warm sandboxes to drain");
                return;
            }

            var group = RedisKeys.WarmGroup(_options.RunnerId);
            var consumer = new GroupConsumer(_db, _logger, RedisKeys.WarmStream, group, _options.RunnerId, fromStart: false);
            await EnsureGroupAsync(group).ConfigureAwait(false);

            _logger.LogInformation("Consuming {Stream} as {Group}", RedisKeys.WarmStream, group);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var entries = await consumer.ReadNewAsync(count: 10, stoppingToken).ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await HandleThenAcknowledgeAsync(
                            () => HandleAsync(entry),
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
        /// Handles one entry and acknowledges it whatever happened. A drain that failed is logged and
        /// dropped, never retried — retrying would only grow this group's pending list with entries
        /// nobody acknowledges, and the old version's sandboxes go anyway: idle ones by the clean-up
        /// (60 s / 600 s), busy ones when their call ends.
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
                logger.LogWarning(ex, "A drain request failed; dropping it");
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

        /// <summary>One entry: drain the old version's warm sandboxes. Anything else on it is ignored.</summary>
        internal async Task HandleAsync(ClaimedEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var tenantId = entry.Get("tenantId") ?? string.Empty;
            var functionId = entry.Get("functionId");
            var drain = entry.Get("drainVersionId");
            if (string.IsNullOrWhiteSpace(functionId) || string.IsNullOrWhiteSpace(drain)) return;

            var drained = await _pool.DrainAsync(tenantId, functionId, drain).ConfigureAwait(false);
            if (drained > 0)
            {
                _logger.LogInformation(
                    "Drained {Count} idle warm sandbox(es) of function {FunctionId} version {VersionId}",
                    drained, functionId, drain);
            }
        }
    }
}
