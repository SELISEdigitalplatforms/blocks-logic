using Blocks.Secrets;
using FluentValidation;
using Functions.DomainService.Consumers;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Functions.DomainService.Utils
{
    public static class ApplicationServiceCollectionExtensions
    {
        /// <summary>Called by both Api and Worker: repositories and the services either side needs.</summary>
        public static IServiceCollection AddFunctionsServices(this IServiceCollection services)
        {
            // IHttpContextAccessor backs FunctionInvocationService's anonymous HTTP auth path
            // (mirroring WorkflowExecutionService's own use of it for webhooks) and is a no-op
            // to add twice, so TryAdd rather than assuming the host already registered it.
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // ISecretService (DECISIONS D6). The Api host must also register
            // SecretExceptionFilter (services.Configure<MvcOptions>(o => o.Filters.Add<SecretExceptionFilter>()))
            // per the SDK's own setup instructions — that half is MVC-specific and lives in
            // Program.cs, not here.
            services.AddBlocksSecrets();

            services.AddSingleton<IFunctionRepository, FunctionRepository>();
            services.AddSingleton<IFunctionVersionRepository, FunctionVersionRepository>();
            services.AddSingleton<IFunctionRunRepository, FunctionRunRepository>();
            services.AddSingleton<IFunctionRunStatsRepository, FunctionRunStatsRepository>();
            services.AddSingleton<IFunctionRunLogRepository, FunctionRunLogRepository>();
            services.AddSingleton<IFunctionBuildRepository, FunctionBuildRepository>();
            services.AddSingleton<IFunctionAuditRepository, FunctionAuditRepository>();

            services.AddSingleton<ITenantAccessor, TenantAccessor>();
            services.AddSingleton<IFunctionAuditService, FunctionAuditService>();
            services.AddSingleton<IFunctionAdmissionService, FunctionAdmissionService>();
            services.AddSingleton<IFunctionAuthorizationService, FunctionAuthorizationService>();
            services.AddSingleton<IFunctionBuildService, FunctionBuildService>();
            services.AddSingleton<IFunctionService, FunctionService>();
            services.AddSingleton<IFunctionVersionRetentionService, FunctionVersionRetentionService>();
            services.AddSingleton<IFunctionDeploymentService, FunctionDeploymentService>();
            services.AddSingleton<IFunctionRunService, FunctionRunService>();
            services.AddSingleton<IFunctionInvocationService, FunctionInvocationService>();

            // Scoped, not singleton: it constructor-injects the Secrets SDK's own
            // ISecretService, which AddBlocksSecrets() registers scoped. A singleton here
            // would capture a scoped dependency for the app's whole lifetime — the classic
            // captive-dependency bug, and one the DI container's own validation catches at
            // startup rather than at first use.
            services.AddScoped<IFunctionSecretCatalogService, FunctionSecretCatalogService>();
            services.AddSingleton<IOutputActionProcessor, OutputActionProcessor>();
            services.AddSingleton<ISecretResolver>(sp => SelectSecretResolver(sp));
            services.AddHttpClient(nameof(BlocksOsHttpSecretResolver));
            services.AddHttpClient(nameof(OutputActionProcessor));

            services.AddValidator<CreateFunctionRequestDto, CreateFunctionRequestValidator>();
            services.AddValidator<UpdateFunctionRequestDto, UpdateFunctionRequestValidator>();
            services.AddValidator<SaveFunctionRequestDto, SaveFunctionRequestValidator>();
            services.AddValidator<TestFunctionRequestDto, TestFunctionRequestValidator>();
            services.AddValidator<DeployFunctionRequestDto, DeployFunctionRequestValidator>();
            services.AddValidator<RollbackFunctionRequestDto, RollbackFunctionRequestValidator>();

            return services;
        }

        /// <summary>Worker only: the two consumers that apply results, plus the retry sweep.</summary>
        public static IServiceCollection AddFunctionsWorkerServices(this IServiceCollection services)
        {
            services.AddHostedService<FunctionResultConsumer>();
            services.AddHostedService<FunctionBuildResultConsumer>();
            services.AddHostedService<FunctionRetryScheduler>();
            // Retention for the streams themselves. Reading an entry does not remove it, and
            // nothing else trims them, so without this the streams are the one unbounded thing
            // in the design.
            services.AddHostedService<FunctionStreamTrimmer>();
            return services;
        }

        private static ISecretResolver SelectSecretResolver(IServiceProvider sp)
        {
            var configuration = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var resolverName = configuration["Functions:SecretResolver"];

            // Defaults to the HTTP fallback rather than the in-process SDK: the in-process
            // resolver's only value store is Azure Key Vault (DECISIONS.md open question #3),
            // which is not confirmed for every environment this runs in, whereas the HTTP path
            // degrades to a clear, attributable per-action failure instead of every secret
            // resolution throwing at startup.
            return string.Equals(resolverName, "nuget", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<NugetSecretResolver>(sp)
                : ActivatorUtilities.CreateInstance<BlocksOsHttpSecretResolver>(sp);
        }

        private static void AddValidator<TRequest, TValidator>(this IServiceCollection services)
            where TValidator : class, IValidator<TRequest>
            => services.AddSingleton<IValidator<TRequest>, TValidator>();
    }
}
