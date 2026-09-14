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

        public HeartbeatService(
            IDatabase db,
            HostBudget budget,
            StartupGuard guard,
            IOptions<RunnerOptions> options,
            ILogger<HeartbeatService> logger)
        {
            _db = db;
            _budget = budget;
            _guard = guard;
            _options = options.Value;
            _logger = logger;
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

                    await _db.HashSetAsync(key,
                    [
                        new HashEntry("runnerId", _options.RunnerId),
                        new HashEntry("version", version),
                        new HashEntry("active", _budget.Active.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("capacity", _budget.Capacity.ToString(CultureInfo.InvariantCulture)),
                        new HashEntry("gvisorOk", readiness.GvisorOk ? "true" : "false"),
                        new HashEntry("healthy", readiness.Healthy ? "true" : "false"),
                        new HashEntry("detail", readiness.Summary),
                        new HashEntry("observedAt", DateTimeOffset.UtcNow.ToString("O")),
                    ]).ConfigureAwait(false);

                    await _db.KeyExpireAsync(key, RedisKeys.HeartbeatTtl).ConfigureAwait(false);
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
            }
            catch (RedisException)
            {
                // Shutting down anyway; the TTL will clear it.
            }
        }
    }
}
