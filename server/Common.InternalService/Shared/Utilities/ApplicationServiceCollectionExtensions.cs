using Blocks.Extension.DependencyInjection;
using Common.InternalService.Language;
using Common.InternalService.Monitor;
using Common.InternalService.Shared.Services;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.People;
using DomainService.Projects;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Common.InternalService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterCommonInternalServices ( this IServiceCollection serviceCollection )
        {
            #region Language
            serviceCollection.AddSingleton<ILanguageManagementService, LanguageManagementService>();
            serviceCollection.AddSingleton<ILanguageRepository, LanguageRepository>();
            #endregion

            #region Configuration
            serviceCollection.AddSingleton<IConfigurationService, ConfigurationService>();
            serviceCollection.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
            #endregion

            #region Monitor
            serviceCollection.AddSingleton<IMonitorObservabilityService, MonitorObservabilityService>();
            serviceCollection.AddSingleton<IMonitorConfigurationService, MonitorConfigurationService>();
            serviceCollection.AddSingleton<IMonitorConfigurationRepoService, MonitorConfigurationRepoService>();
            serviceCollection.AddSingleton<IMonitorIncidentService, MonitorIncidentService>();
            serviceCollection.AddSingleton<IMonitorIncidentRepoService, MonitorIncidentRepoService>();
            serviceCollection.AddSingleton<IMonitorPingService, MonitorPingService>();
            serviceCollection.AddSingleton<IMonitorPingRepoService, MonitorPingRepoService>();

            serviceCollection.AddTransient<IValidator<SaveMonitorConfigurationRequest>, SaveMonitorConfigurationRequestValidator>();
            serviceCollection.AddTransient<IValidator<UpdateMonitorConfigurationRequest>, UpdateMonitorConfigurationRequestValidator>();
            #endregion

            #region Identifier
            // Register services
            serviceCollection.AddSingleton<IProjectManagementService, ProjectManagementService>();
            serviceCollection.AddSingleton<IProjectRepository, ProjectRepository>();

            serviceCollection.AddSingleton<IPeopleService, PeopleService>();
            serviceCollection.AddSingleton<IPeopleRepository, PeopleRepository>();
            serviceCollection.AddSingleton<IServiceManagement, ServiceManagement>();
            serviceCollection.AddSingleton<IServiceManagementRepository, ServiceManagementRepository>();
            #endregion
      
        }
    }
}
