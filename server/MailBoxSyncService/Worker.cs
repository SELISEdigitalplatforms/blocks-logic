using MailBoxSyncService.Services;

namespace MailBoxSyncService
{
    public class Worker : BackgroundService
    {
        private const int DefaultPollIntervalSeconds = 30;

        private readonly IMailRepository _mailRepository;
        private readonly IMailBoxSyncService _mailBoxSyncService;
        private readonly ILogger<Worker> _logger;
        private readonly TimeSpan _pollInterval;

        public Worker(
            IMailRepository mailRepository,
            IMailBoxSyncService mailBoxSyncService,
            ILogger<Worker> logger,
            IConfiguration configuration)
        {
            _mailRepository = mailRepository;
            _mailBoxSyncService = mailBoxSyncService;
            _logger = logger;
            var seconds = configuration.GetValue("MailBoxSync:PollIntervalSeconds", DefaultPollIntervalSeconds);
            if (seconds <= 0)
            {
                seconds = DefaultPollIntervalSeconds;
            }
            _pollInterval = TimeSpan.FromSeconds(seconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_pollInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncAllTenantsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error during mailbox sync cycle");
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task SyncAllTenantsAsync(CancellationToken stoppingToken)
        {
            var tenants = await _mailRepository.GetTenantsAsync();

            foreach (var tenant in tenants)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                var configs = await _mailRepository.GetImapConfigurationsAsync(tenant);
                foreach (var config in configs)
                {
                    try
                    {
                        await _mailBoxSyncService.SyncInboxAsync(config, tenant.TenantId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing inbox for tenant '{TenantId}' using config '{ConfigId}'", tenant.TenantId, config.ItemId);
                    }
                }
            }
        }
    }
}
