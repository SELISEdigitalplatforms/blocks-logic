using DataAccess.InternalService.Notifications;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Strategies;
using MailBoxSyncService.Services;
using System.Collections.Concurrent;

namespace MailBoxSyncService
{
    public class Worker : BackgroundService
    {
        private const int DefaultPollIntervalSeconds = 30;

        /// <summary>Failures in a row before the admins are told. Once per streak.</summary>
        internal const int FailureStreakThreshold = 5;

        private readonly IMailRepository _mailRepository;
        private readonly IMailBoxSyncService _mailBoxSyncService;
        private readonly IInboundMailPollerRegistry _pollerRegistry;
        private readonly ILogger<Worker> _logger;
        private readonly TimeSpan _pollInterval;
        private readonly IBuiltInNotificationSender? _builtIn;
        private readonly ConcurrentDictionary<string, int> _failureStreaks = new();

        public Worker(
            IMailRepository mailRepository,
            IMailBoxSyncService mailBoxSyncService,
            IInboundMailPollerRegistry pollerRegistry,
            ILogger<Worker> logger,
            IConfiguration configuration,
            IBuiltInNotificationSender? builtInNotifications = null)
        {
            _builtIn = builtInNotifications;
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
                        ResetStreak(tenant.TenantId, config.ItemId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing inbox for tenant '{TenantId}' using config '{ConfigId}'", tenant.TenantId, config.ItemId);
                        await RecordFailureAsync(tenant.TenantId, config.ItemId);
                    }
                }
            }
        }

        /// <summary>A poll succeeded: the next failure starts a new streak.</summary>
        internal void ResetStreak(string tenantId, string configId) => _failureStreaks.TryRemove(StreakKey(tenantId, configId), out _);

        private static string StreakKey(string tenantId, string configId) => $"{tenantId}|{configId}";

        /// <summary>
        /// Counts failures in a row for one tenant and mailbox. The admins are told once, on the
        /// <see cref="FailureStreakThreshold"/>th failure; the next success resets the count, so a
        /// later streak tells them again. Built-in <c>logic-mailbox-sync-failing</c>; never throws.
        /// </summary>
        internal async Task RecordFailureAsync(string tenantId, string configId)
        {
            var count = _failureStreaks.AddOrUpdate(StreakKey(tenantId, configId), 1, (_, current) => current + 1);
            if (count != FailureStreakThreshold || _builtIn is null)
            {
                return;
            }

            try
            {
                await _builtIn.SendToTenantAdminsAsync(
                    tenantId,
                    BuiltInNotificationNames.MailboxSyncFailing,
                    new BuiltInNotificationPayload(
                        "MailboxSyncFailing",
                        "Mailbox sync failing",
                        "Reading an inbound mailbox keeps failing, so new mail is not picked up.",
                        IsSuccess: false,
                        Ids: new Dictionary<string, string?> { ["configId"] = configId }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not notify the admins about mailbox {ConfigId} ({ExceptionType})", configId, ex.GetType().Name);
            }
        }
    }
}
