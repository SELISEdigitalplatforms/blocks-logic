using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Scheduler.DomainService.Services
{
    public class SchedulerReseedHostedService : BackgroundService
    {
        private readonly IScheduleJobRegistry _scheduleJobRegistry;
        private readonly ILogger<SchedulerReseedHostedService> _logger;

        public SchedulerReseedHostedService(
            IScheduleJobRegistry scheduleJobRegistry,
            ILogger<SchedulerReseedHostedService> logger)
        {
            _scheduleJobRegistry = scheduleJobRegistry;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Scheduler reseed: rebuilding Hangfire recurring jobs from Mongo.");
                await _scheduleJobRegistry.RegisterAllAsync();
                _logger.LogInformation("Scheduler reseed completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler reseed failed at startup. Registration will rely on the Worker consumer queue and the next restart.");
            }
        }
    }
}
