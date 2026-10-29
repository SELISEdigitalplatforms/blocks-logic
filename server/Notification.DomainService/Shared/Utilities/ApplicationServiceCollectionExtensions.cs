using Blocks.Genesis.Notifications;
using DomainService.Configuration.Services;
using DomainService.Configuration.Validators;
using DomainService.Notification;
using DomainService.Notification.Push;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        /// <summary>
        /// Inbox, hub handler, Notify, push devices, push tests and status, and the push worker's services. Sending itself is
        /// genesis <c>AddBlocksNotifications()</c>, called by each host. The <c>PushRequested</c> consumer is registered
        /// by the Worker host only.
        /// </summary>
        public static void RegisterAllNotificationApplicationServices(this IServiceCollection serviceCollection)
        {
            #region Services
            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.AddSingleton<INotificationService, NotificationService>();
            serviceCollection.AddSingleton<INotifyService, NotifyService>();
            serviceCollection.AddSingleton<INotifierSendGuard, NotifierSendGuard>();
            serviceCollection.AddSingleton<INotificationRepository, NotificationRepository>();
            serviceCollection.AddSingleton<INotificationUserRepository, NotificationUserRepository>();
            serviceCollection.AddSingleton<INotificationHubHandler, NotificationHubHandler>();
            serviceCollection.AddSingleton<IConfigurationService, ConfigurationService>();
            serviceCollection.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
            #endregion

            #region Push
            serviceCollection.AddHttpClient(FcmAccessTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
            serviceCollection.AddSingleton<IPushSettingsStore, PushSettingsStore>();
            serviceCollection.AddSingleton<IPushCredentialStore, PushCredentialStore>();
            serviceCollection.AddSingleton<IPushDeviceStore, PushDeviceStore>();
            serviceCollection.AddSingleton<IPushDeviceService, PushDeviceService>();
            serviceCollection.AddSingleton<IPushLocalizationRepository, PushLocalizationRepository>();
            serviceCollection.AddSingleton<IPushMessageComposer, PushMessageComposer>();
            serviceCollection.AddSingleton<IPushDedupStore, PushDedupStore>();
            serviceCollection.AddSingleton<IFcmAccessTokenProvider, FcmAccessTokenProvider>();
            serviceCollection.AddSingleton<IFcmClient, FcmClient>();
            serviceCollection.AddSingleton<IPushRetryDelay, PushRetryDelay>();
            serviceCollection.AddSingleton<IFcmPushSender, FcmPushSender>();
            serviceCollection.AddSingleton<IPushOptionsStore, PushOptionsStore>();
            serviceCollection.AddSingleton<IPushStatusStore, PushStatusStore>();
            serviceCollection.AddSingleton<IPushUserRepository, PushUserRepository>();
            serviceCollection.AddSingleton<IPushTestRunner, PushTestRunner>();
            serviceCollection.AddSingleton<IPushTestService, PushTestService>();
            serviceCollection.AddSingleton<IPushStatusService, PushStatusService>();
            serviceCollection.AddSingleton<IPushDeviceAdminService, PushDeviceAdminService>();
            serviceCollection.AddSingleton<IPushWebConfigService, PushWebConfigService>();
            serviceCollection.AddSingleton<IPushRequestHandler, PushRequestHandler>();
            #endregion

            #region Validators
            serviceCollection.AddTransient<IValidator<Subscription>, AddSubscriptionRequestValidator>();
            serviceCollection.AddTransient<IValidator<NotifyRequest>, NotifyRequestValidator>();
            serviceCollection.AddTransient<IValidator<Configuration.SaveConfigurationRequest>, ConfigurationValidator>();
            #endregion
        }
    }
}
