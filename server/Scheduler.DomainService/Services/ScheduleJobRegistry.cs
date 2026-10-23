using DataAccess.InternalService.Notifications;
using Hangfire;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Repositories;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Services
{
    public class ScheduleJobRegistry : IScheduleJobRegistry
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly SchedulePublisherService _schedulePublisherService;
        private readonly ILogger<ScheduleJobRegistry> _logger;
        private readonly IBuiltInNotificationSender? _builtIn;

        public ScheduleJobRegistry(IScheduleRepository scheduleRepository,
                                   SchedulePublisherService scheduledEventPublisher,
                                   ILogger<ScheduleJobRegistry> logger,
                                   IBuiltInNotificationSender? builtInNotifications = null)
        {
            _builtIn = builtInNotifications;
            _scheduleRepository = scheduleRepository;
            _schedulePublisherService = scheduledEventPublisher;
            _logger = logger;
        }

        public async Task RegisterAllAsync()
        {
            var schedules = await _scheduleRepository.GetSchedulesFromAllTenantsAsync();

            foreach (var schedule in schedules)
            {
                RegisterSchedulesByTenant(schedule.Schedules, schedule.TenantId);
            }

            await Task.CompletedTask;
        }

        public async Task ApplyUpsertAsync(ScheduleJobUpsertedEvent @event)
        {
            var schedule = await _scheduleRepository.GetByIdAsync(@event.ItemId, @event.TenantId);

            if (schedule is null)
            {
                _logger.LogInformation("Schedule {ItemId} for tenant {TenantId} no longer exists; nothing to register (race with delete).",
                    @event.ItemId, @event.TenantId);
                return;
            }

            if (!schedule.IsActive)
            {
                RecurringJob.RemoveIfExists($"{@event.ItemId}-{@event.TenantId}");
                return;
            }

            var options = new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            };

            try
            {
                RecurringJob.AddOrUpdate(
                    recurringJobId: $"{@event.ItemId}-{@event.TenantId}",
                    methodCall: () => _schedulePublisherService.Publish(@event.ItemId, @event.TenantId),
                    cronExpression: schedule.CronExpression,
                    options: options);
            }
            catch (Exception)
            {
                // Told once per schedule (throttled), then the original failure goes on to the queue's retry.
                await ScheduleRegistrationNotifier.NotifyAsync(
                    _builtIn, @event.TenantId,
                    schedule.Kind == ScheduleKind.Internal ? schedule.CreatedBy : null, // workflow schedules only
                    @event.ItemId, workflowId: null);
                throw;
            }
        }

        public Task ApplyDeleteAsync(ScheduleJobDeletedEvent @event)
        {
            RecurringJob.RemoveIfExists($"{@event.ItemId}-{@event.TenantId}");
            return Task.CompletedTask;
        }

        private void RegisterSchedulesByTenant(List<Schedule> schedules, string tenantId)
        {
            foreach (var schedule in schedules)
            {
                var options = new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Utc
                };

                RecurringJob.AddOrUpdate(
                    recurringJobId: $"{schedule.ItemId}-{tenantId}",
                    methodCall: () => _schedulePublisherService.Publish(schedule.ItemId, tenantId),
                    cronExpression: schedule.CronExpression,
                    options: options);
            }
        }
    }
}
