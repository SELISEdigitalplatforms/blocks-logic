using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Strategies;
using MailBoxSyncService.Services;

namespace MailBoxSyncService
{
    public class Worker : BackgroundService
    {
        private const int DefaultPollIntervalSeconds = 30;

        private readonly IMailRepository _mailRepository;
        private readonly IMailBoxSyncService _mailBoxSyncService;
        private readonly IInboundMailPollerRegistry _pollerRegistry;
        private readonly ILogger<Worker> _logger;
        private readonly TimeSpan _pollInterval;

        public Worker(
            IMailRepository mailRepository,
            IMailBoxSyncService mailBoxSyncService,
            IInboundMailPollerRegistry pollerRegistry,
            ILogger<Worker> logger,
            IConfiguration configuration)
        {
            _mailRepository = mailRepository;
            _mailBoxSyncService = mailBoxSyncService;
            _pollerRegistry = pollerRegistry;
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

                List<MailServerConfiguration> configs;
                try
                {
                    configs = await _mailRepository.GetImapConfigurationsAsync(tenant);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading inbound mail configurations for tenant '{TenantId}'", tenant.TenantId);
                    continue;
                }
                foreach (var config in configs)
                {
                    // Provider first, and a miss is a skip rather than a throw: a record for a
                    // provider with no inbound support must not stop the tenant's other
                    // configurations, and nothing is connected or read before this point.
                    if (!_pollerRegistry.TryResolve(config.Provider, out var poller))
                    {
                        _logger.LogWarning(
                            "Skipping inbound config '{ConfigId}' for tenant '{TenantId}': provider {Provider} has no inbound poller registered.",
                            config.ItemId,
                            tenant.TenantId,
                            config.Provider);
                        continue;
                    }

                    try
                    {
                        await poller.PollAsync(config, tenant.TenantId, stoppingToken);
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
