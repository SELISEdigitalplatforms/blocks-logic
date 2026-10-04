using Blocks.Extension.DependencyInjection;
//using Captcha.DomainService.Captcha;
//using Captcha.DomainService.Configuration;
//using Captcha.DomainService.Utilities;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.People;
using DomainService.Projects;
using DomainService.Storage;
using FluentValidation;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Storage.DomainService.Storage;
using Storage.DomainService.Storage.Validators;

namespace DomainService.Shared
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void AddApplicationServices(this IServiceCollection services)
        {
            // Register services
            services.AddSingleton<IProjectManagementService, ProjectManagementService>();
            services.AddSingleton<IProjectRepository, ProjectRepository>();

            services.AddSingleton<IPeopleService, PeopleService>();
            services.AddSingleton<IPeopleRepository, PeopleRepository>();
            services.AddSingleton<IServiceManagement, ServiceManagement>();
            services.AddSingleton<IServiceManagementRepository, ServiceManagementRepository>();

            // Drivers
            services.AddTransient<IValidator<UpdateFileRequest>, UpdateFileRequestValidator>(); 
            services.AddTransient<AwsS3CompatibleStorageService>();

            services.RegisterBlocksStorageServices();
            services.RegisterAllMailApplicationServices();

            // Captcha
            //services.AddTransient<IValidator<CreateCaptchaRequest>, CreateCaptchaCommandValidator>();
            //services.AddTransient<IValidator<SubmitCaptchaRequest>, SubmitCaptchaCommandValidator>();
            //services.AddSingleton<ICaptchaService, CaptchaService>();
            //services.AddSingleton<ICaptchaConfigurationService, CaptchaConfigurationService>();
            //services.AddSingleton<ICaptchaConfigurationRepository, CaptchaConfigurationRepository>();
            //services.AddSingleton<ICaptchaGeneratorProvider, CaptchaGeneratorProvider>();
            //services.AddSingleton<IContextCaptchaIdGeneratorService, ContextCaptchaIdGeneratorService>();
            //services.AddSingleton<ICaptchaVerificationServiceProvider, CaptchaVerificationServiceProvider>();
            //services.AddSingleton<ICaptchaProcessor, CaptchaProcessor>();
            //services.AddSingleton<IRecaptchaConfigFactory, RecaptchaConfigFactory>();
            //services.AddSingleton<IHttpClientService, HttpClientService>();
            //services.AddSingleton<ReCaptchaVerificationService>();
        }
    }
}
