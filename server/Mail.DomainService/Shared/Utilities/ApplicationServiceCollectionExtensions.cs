using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Blocks.Genesis;
using Mail.DomainService.Template.Services;
using Mail.DomainService.Template;
using Mail.DomainService.Template.Validators;

namespace Mail.DomainService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterAllMailApplicationServices(this IServiceCollection services)
        {
            services.AddTransient<IValidator<Template.Template>, TemplateValidator>();

            services.AddSingleton<ITemplateService, TemplateService>();
            services.AddSingleton<ITemplateRepository, TemplateRepository>();



            services.AddSingleton<IMailRepository, MailRepository>();
            services.AddSingleton<SmtpClientProvider>();

            // Outbound strategies. Adding a provider is one more registration here plus its
            // sender; nothing in SendMailService changes.
            // TryAddEnumerable: the API and the worker both call this method more than once.
            // IEnumerable<IOutboundMailSender> returns every registration, and a second copy of
            // the same provider makes OutboundMailSenderRegistry throw while building its dictionary.
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboundMailSender, AmazonSesMailSender>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboundMailSender, ZohoMailSender>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboundMailSender, Office365MailSender>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboundMailSender, GmailMailSender>());
            services.TryAddSingleton<IOutboundMailSenderRegistry, OutboundMailSenderRegistry>();

            services.RegisterOffice365TokenServices();
            services.TryAddSingleton<Office365SmtpClient>();
            services.TryAddSingleton<Office365GraphMailSender>();

            // A plain client: no Graph SDK middleware, so nothing retries a send that may already
            // have been accepted. The timeout bounds one request, and a large draft upload is
            // many requests.
            services.AddHttpClient(Office365GraphMailSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(100));
            services.AddTransient<MailKitSmtpClient>();
            services.AddTransient<MicrosoftSmtpClient>();
            services.AddSingleton<ISendMailService, SendMailService>();

            // Registered unconditionally rather than via TryAdd: RegisterAllMailApplicationServices
            // has five call sites and runs twice inside the Api, so any presence-detection scheme
            // here would resolve differently depending on which host registered first. The resolver
            // takes IStorageDriverService as an optional dependency and reports its absence itself.
            services.AddOptions<MailAttachmentOptions>().BindConfiguration(MailAttachmentOptions.SectionName);
            services.AddOptions<MailStatusEventOptions>().BindConfiguration(MailStatusEventOptions.SectionName);
            services.AddHttpClient();
            services.AddSingleton<IMailAttachmentResolver, StorageMailAttachmentResolver>();
            services.AddSingleton<IMailService, MailService>();

            services.AddTransient<IValidator<MailToBeSent>, EmailValidator>();
            services.AddSingleton<CommonEmailValidator>();
        }

        /// <summary>
        /// The Office 365 token path alone, for a host that needs tokens without the rest of the
        /// mail services — the inbound IMAP poller. The caller must also call
        /// <c>AddBlocksSecrets()</c>, which supplies the scoped <c>ISecretService</c>.
        /// </summary>
        /// <remarks>
        /// The provider opens its own DI scope for ISecretService, so it is safe as a singleton
        /// even though ISecretService is scoped. TryAdd because
        /// <see cref="RegisterAllMailApplicationServices"/> calls this and runs more than once.
        /// </remarks>
        public static IServiceCollection RegisterOffice365TokenServices(this IServiceCollection services)
        {
            services.TryAddSingleton<IOffice365TokenAcquirer, AzureOffice365TokenAcquirer>();
            services.TryAddSingleton<ISystemClock, SystemClock>();
            services.Configure<Office365TokenCacheOptions>(_ => { });
            services.TryAddSingleton<IOffice365TokenProvider, CachingOffice365TokenProvider>();
            return services;
        }
    }
}
