using System.Globalization;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// Publishes this runner's state so the control plane can see the fleet.
    /// <para>
    /// The hash carries a 15 second TTL and is refreshed every 5, so a runner that dies simply
    /// disappears rather than lingering as a stale entry that looks alive.
    /// </para>
    /// </summary>
    public sealed class HeartbeatService : BackgroundService
    {
        private readonly IDatabase _db;
        private readonly HostBudget _budget;
        private readonly StartupGuard _guard;
        private readonly RunnerOptions _options;
        private readonly ILogger<HeartbeatService> _logger;
        private readonly Sandbox.WarmPool? _warm;
        private readonly FleetCapacity? _fleet;

        public HeartbeatService(
            IDatabase db,
            HostBudget budget,
            StartupGuard guard,
            IOptions<RunnerOptions> options,
            ILogger<HeartbeatService> logger,
            Sandbox.WarmPool? warm = null,
            FleetCapacity? fleet = null)
        {
            _fleet = fleet;
            _db = db;
            _budget = budget;
            _guard = guard;
            _options = options.Value;
            _logger = logger;
            _warm = warm;
        }

        /// <summary>The most recent readiness verdict, shared with the processing loops.</summary>
        public HostReadiness? Latest { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var version = typeof(HeartbeatService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var key = RedisKeys.Runner(_options.RunnerId);
            var interval = TimeSpan.FromMilliseconds(_options.HeartbeatMs);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var readiness = await _guard.CheckAsync(stoppingToken).ConfigureAwait(false);
                    Latest = readiness;

                    // Warm sandboxes are reported apart from `active`: an idle one holds memory
                    // and no slot, so neither number alone says how full this runner is.
                    var (warmTotal, warmBusy, warmIdle) = _warm?.Counts ?? (0, 0, 0);

                    await _db.HashSetAsync(key,
                    [
                        new HashEntry("runnerId", _options.RunnerId),
                        new HashEntry("version", version),
                        new HashEntry("active", _budget.Active.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("capacity", _budget.Capacity.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("gvisorOk", readiness.GvisorOk ? "true" : "false"),
                        new HashEntry("healthy", readiness.Healthy ? "true" : "false"),
                        new HashEntry("detail", readiness.Summary),
                        new HashEntry("warmTotal", warmTotal.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("warmBusy", warmBusy.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("warmIdle", warmIdle.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("observedAt", DateTimeOffset.UtcNow.ToString("O")),
                    ]).ConfigureAwait(false);

                    await _db.KeyExpireAsync(key, RedisKeys.HeartbeatTtl).ConfigureAwait(false);

                    // The fleet's capacity for the tenant share, read here and not per run.
                    if (_fleet is not null)
                    {
                        await _fleet.JoinAsync(_db).ConfigureAwait(false);
                        await _fleet.RefreshAsync(_db).ConfigureAwait(false);
                    }

                    // Which base image this host builds on, for the control plane's build cache. Old
                    // entries (no runner announced them for a day) are dropped as we go.
                    var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await _db.SortedSetAddAsync(RedisKeys.BaseImages, _options.BaseImage, nowSeconds).ConfigureAwait(false);
                    await _db.SortedSetRemoveRangeByScoreAsync(RedisKeys.BaseImages, double.NegativeInfinity, nowSeconds - 86_400)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Heartbeat failed: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try
            {
                await _db.KeyDeleteAsync(key).ConfigureAwait(false);
                if (_fleet is not null) await _fleet.LeaveAsync(_db).ConfigureAwait(false);
            }
            catch (RedisException)
            {
                // Shutting down anyway; the TTL will clear it.
            }
        }
    }
}
