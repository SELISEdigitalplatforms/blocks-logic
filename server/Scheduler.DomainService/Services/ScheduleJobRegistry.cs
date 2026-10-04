using Hangfire;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Repositories;

namespace Scheduler.DomainService.Services
{
    public class ScheduleJobRegistry : IScheduleJobRegistry
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly SchedulePublisherService _schedulePublisherService;
        private readonly ILogger<ScheduleJobRegistry> _logger;

        public ScheduleJobRegistry(IScheduleRepository scheduleRepository,
                                   SchedulePublisherService scheduledEventPublisher,
                                   ILogger<ScheduleJobRegistry> logger)
        {
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

            RecurringJob.AddOrUpdate(
                recurringJobId: $"{@event.ItemId}-{@event.TenantId}",
                methodCall: () => _schedulePublisherService.Publish(@event.ItemId, @event.TenantId),
                cronExpression: schedule.CronExpression,
                options: options);
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
