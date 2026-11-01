using DataAccess.InternalService.Notifications;
using Hangfire;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Services
{
    public class ScheduleJobRegistry : IScheduleJobRegistry
    {
        private readonly IScheduleRepository _scheduleRepository;
        private readonly SchedulePublisherService _schedulePublisherService;
        private readonly IRecurringJobManager _recurringJobs;
        private readonly ILogger<ScheduleJobRegistry> _logger;
        private readonly IBuiltInNotificationSender? _builtIn;

        public ScheduleJobRegistry(IScheduleRepository scheduleRepository,
                                   SchedulePublisherService scheduledEventPublisher,
                                   IRecurringJobManager recurringJobs,
                                   ILogger<ScheduleJobRegistry> logger,
                                   IBuiltInNotificationSender? builtInNotifications = null)
        {
            _builtIn = builtInNotifications;
            _scheduleRepository = scheduleRepository;
            _schedulePublisherService = scheduledEventPublisher;
            _recurringJobs = recurringJobs;
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
                _recurringJobs.RemoveIfExists($"{@event.ItemId}-{@event.TenantId}");
                return;
            }

            if (!ScheduleTimeZones.TryResolve(schedule.TimeZone, out var timeZone))
            {
                // Fail loud, never silently UTC: the job is dropped and the owner told. No rethrow, since a retry
                // cannot bring missing zone data.
                _recurringJobs.RemoveIfExists($"{@event.ItemId}-{@event.TenantId}");
                _logger.LogError("Schedule {ItemId} (tenant {TenantId}) has unknown time zone {TimeZone}; not registered.",
                    @event.ItemId, @event.TenantId, schedule.TimeZone);
                await ScheduleRegistrationNotifier.NotifyAsync(_builtIn, @event.TenantId, null, @event.ItemId, workflowId: null);
                return;
            }

            var options = new RecurringJobOptions
            {
                TimeZone = timeZone
            };

            try
            {
                _recurringJobs.AddOrUpdate(
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
            _recurringJobs.RemoveIfExists($"{@event.ItemId}-{@event.TenantId}");
            return Task.CompletedTask;
        }

        private void RegisterSchedulesByTenant(List<Schedule> schedules, string tenantId)
        {
            foreach (var schedule in schedules)
            {
                if (!ScheduleTimeZones.TryResolve(schedule.TimeZone, out var timeZone))
                {
                    _logger.LogError("Schedule {ItemId} (tenant {TenantId}) has unknown time zone {TimeZone}; not registered.",
                        schedule.ItemId, tenantId, schedule.TimeZone);
                    continue;
                }

                var options = new RecurringJobOptions
                {
                    TimeZone = timeZone
                };

                _recurringJobs.AddOrUpdate(
                    recurringJobId: $"{schedule.ItemId}-{tenantId}",
                    methodCall: () => _schedulePublisherService.Publish(schedule.ItemId, tenantId),
                    cronExpression: schedule.CronExpression,
                    options: options);
            }
        }
    }
}
