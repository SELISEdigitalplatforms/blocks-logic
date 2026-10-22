using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Scheduler.DomainService.Services
{
    /// <summary>
    /// Rebuilds the Hangfire recurring jobs from Mongo at startup. Scheduler.Worker is the only process that fires
    /// schedules, so a failed reseed is retried and, if it keeps failing, the process exits with code 1 for
    /// Kubernetes or systemd to restart it, instead of running on with no schedules registered.
    /// </summary>
    public class SchedulerReseedHostedService : BackgroundService
    {
        public const int MaxAttempts = 5;

        private readonly IScheduleJobRegistry _scheduleJobRegistry;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<SchedulerReseedHostedService> _logger;

        public SchedulerReseedHostedService(
            IScheduleJobRegistry scheduleJobRegistry,
            IHostApplicationLifetime lifetime,
            TimeProvider timeProvider,
            ILogger<SchedulerReseedHostedService> logger)
        {
            _scheduleJobRegistry = scheduleJobRegistry;
            _lifetime = lifetime;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>The wait after a failed attempt: 2, 4, 8, then 16 seconds.</summary>
        public static TimeSpan DelayAfterAttempt(int attempt) => TimeSpan.FromSeconds(1 << attempt);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scheduler reseed: rebuilding Hangfire recurring jobs from Mongo.");

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    await _scheduleJobRegistry.RegisterAllAsync();
                    _logger.LogInformation("Scheduler reseed completed on attempt {Attempt}.", attempt);
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts)
                {
                    _logger.LogWarning(ex, "Scheduler reseed attempt {Attempt} of {MaxAttempts} failed; retrying.", attempt, MaxAttempts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduler reseed failed after {MaxAttempts} attempts; stopping so the process is restarted.", MaxAttempts);
                    Environment.ExitCode = 1;
                    _lifetime.StopApplication();
                    return;
                }

                try
                {
                    await Task.Delay(DelayAfterAttempt(attempt), _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
