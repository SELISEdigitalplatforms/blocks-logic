using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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
