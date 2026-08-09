using Blocks.Driver.Captcha;
using Blocks.Driver.Iam;
using Blocks.Driver.Language;
using Blocks.Driver.Mfa;
using Blocks.Driver.Monitor;
using Blocks.Driver.Storage;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using Captcha.DomainService.Utilities;
using Common.InternalService.Shared.Utilities;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
//using DomainService.People;
using DomainService.Projects;
using FluentValidation;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extension.DependencyInjection;

public static class DriverServiceExtension
{
    /// <summary>
    /// Registers the Blocks.Driver facade covering Captcha, MFA, Identifier, IAM, Storage,
    /// Language and Monitor, along with the underlying services each one depends on.
    /// </summary>
    public static void RegisterBlocksDriver(this IServiceCollection services)
    {
        // Captcha.Driver/Iam.Driver/Mfa.Driver on disk are unmaintained (net9.0, non-CPM package
        // versions) and left untouched; the same DomainService wiring they perform is registered
        // here directly instead.
        RegisterCaptchaServices(services);
        services.RegisterSharedServices();
        services.RegisterAllServices();
        services.AddSingleton<ICaptchaDriverService, CaptchaDriverService>();
        services.AddSingleton<IMfaDriverService, MfaDriverService>();
        services.AddSingleton<IIamDriverService, IamDriverService>();

        // Identifier has no existing driver package, so its services are registered directly.
       // services.AddSingleton<IPeopleService, PeopleService>();
       // services.AddSingleton<IPeopleRepository, PeopleRepository>();
        services.AddSingleton<IProjectManagementService, ProjectManagementService>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IServiceManagement, ServiceManagement>();
        services.AddSingleton<IServiceManagementRepository, ServiceManagementRepository>();
       // services.AddSingleton<IIdentifierDriverService, IdentifierDriverService>();

        // Storage, Language and Monitor come from Common.InternalService.
        services.RegisterCommonInternalServices();
        services.AddSingleton<IStorageDriverService, StorageDriverService>();
        services.AddSingleton<ILanguageDriverService, LanguageDriverService>();
        services.AddSingleton<IMonitorDriverService, MonitorDriverService>();
    }

    private static void RegisterCaptchaServices(IServiceCollection services)
    {
        services.AddTransient<IValidator<CreateCaptchaRequest>, CreateCaptchaCommandValidator>();
        services.AddTransient<IValidator<SubmitCaptchaRequest>, SubmitCaptchaCommandValidator>();

        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddSingleton<ICaptchaConfigurationService, CaptchaConfigurationService>();
        services.AddSingleton<ICaptchaConfigurationRepository, CaptchaConfigurationRepository>();
        services.AddSingleton<ICaptchaGeneratorProvider, CaptchaGeneratorProvider>();
        services.AddSingleton<IContextCaptchaIdGeneratorService, ContextCaptchaIdGeneratorService>();
        services.AddSingleton<ICaptchaVerificationServiceProvider, CaptchaVerificationServiceProvider>();
        services.AddSingleton<ICaptchaProcessor, CaptchaProcessor>();
        services.AddSingleton<IRecaptchaConfigFactory, RecaptchaConfigFactory>();
        services.AddSingleton<IHttpClientService, HttpClientService>();
        services.AddSingleton<ReCaptchaVerificationService>();
    }
}
