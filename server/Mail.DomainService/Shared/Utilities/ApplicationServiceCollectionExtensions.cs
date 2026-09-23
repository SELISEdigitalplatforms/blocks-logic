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
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboundMailSender, Office365SmtpClient>());
            services.TryAddSingleton<IOutboundMailSenderRegistry, OutboundMailSenderRegistry>();

            // Office 365 token path. The provider opens its own DI scope for ISecretService, so it
            // is safe as a singleton even though ISecretService is scoped.
            services.AddSingleton<IOffice365TokenAcquirer, AzureOffice365TokenAcquirer>();
            services.AddSingleton<ISystemClock, SystemClock>();
            services.Configure<Office365TokenCacheOptions>(_ => { });
            services.AddSingleton<IOffice365TokenProvider, CachingOffice365TokenProvider>();
            services.AddSingleton<Office365SmtpClient>();
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
    }
}
