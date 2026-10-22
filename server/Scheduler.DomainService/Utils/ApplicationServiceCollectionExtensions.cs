using Blocks.Genesis;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Scheduler.DomainService.Consumers;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;

namespace Scheduler.DomainService.Utils
{
    public static class ApplicationServiceCollectionExtensions
    {
        // Called by Api, Worker and Scheduler.Worker.
        public static IServiceCollection AddSchedulerServices(this IServiceCollection services)
        {
            services.AddSingleton<IScheduleRepository, ScheduleRepository>();
            services.AddSingleton<IScheduleService, ScheduleService>();
            return services;
        }

        // Scheduler.Worker ONLY. Hosts Hangfire + the execution (fire) side + the tick callback + consumer + reseed.
        public static IServiceCollection AddSchedulerWorkerServices(this IServiceCollection services)
        {
            services.AddHangfire(c => c.UseMemoryStorage());
            services.AddHangfireServer();
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<SchedulePublisherService>();
            services.AddSingleton<IScheduleJobRegistry, ScheduleJobRegistry>();
            services.AddSingleton<IConsumer<ScheduleJobUpsertedEvent>, ScheduleJobUpsertedConsumer>();
            services.AddSingleton<IConsumer<ScheduleJobDeletedEvent>, ScheduleJobDeletedConsumer>();
            services.AddHostedService<SchedulerReseedHostedService>();
            return services;
        }
    }
}
