using Blocks.Secrets;
using FluentValidation;
using Common.InternalService.Access;
using Functions.DomainService.Consumers;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Functions.DomainService.Utils
{
    public static class ApplicationServiceCollectionExtensions
    {
        /// <summary>Called by both Api and Worker: repositories and the services either side needs.</summary>
        /// <param name="configuration">
        /// Optional, and only read for <c>Functions:QueuePrefix</c> — the Redis namespace this
        /// control plane shares with its runners. Left out, the prefix stays empty and every key
        /// is exactly what it was before the setting existed, so an unconfigured deployment is
        /// unaffected. Pass it wherever two environments share one Redis.
        /// </param>
        public static IServiceCollection AddFunctionsServices(
            this IServiceCollection services, IConfiguration? configuration = null)
        {
            // Set before anything resolves a key: the queue names are read at use, but a consumer
            // group is created from them at startup, and creating it under the wrong name is the
            // one mistake here that is invisible afterwards.
            if (configuration is not null)
            {
                Queue.FunctionQueueKeys.Prefix = configuration["Functions:QueuePrefix"] ?? string.Empty;
            }

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
            services.AddSingleton<IFunctionRunLogRepository, FunctionRunLogRepository>();
            services.AddSingleton<IFunctionBuildRepository, FunctionBuildRepository>();

            // The artifact store. Singleton because its BlobContainerClient is thread-safe and
            // holds a connection pool worth reusing; it takes the tenant per call, never from
            // ambient context, so one instance serves every tenant.
            services.AddSingleton<Storage.IFunctionArtifactStore, Storage.FunctionArtifactStore>();
            services.AddSingleton<IFunctionAuditRepository, FunctionAuditRepository>();

            services.AddSingleton<ITenantAccessor, TenantAccessor>();
            services.AddSingleton<IFunctionAuditService, FunctionAuditService>();
            services.AddSingleton<IFunctionImagePinService, FunctionImagePinService>();
            services.AddSingleton<IFunctionPurgeService, FunctionPurgeService>();
            services.AddSingleton<IFunctionDeletionQueue, FunctionDeletionQueue>();
            services.AddSingleton<IFunctionUsageService, FunctionUsageService>();
            services.AddSingleton<IFunctionImageRecoveryService, FunctionImageRecoveryService>();
            services.AddSingleton<IFunctionAdmissionService, FunctionAdmissionService>();
            services.AddSingleton<IFunctionAuthorizationService, FunctionAuthorizationService>();
            services.AddSingleton<IFunctionBuildService, FunctionBuildService>();
            services.AddSingleton<IFunctionService, FunctionService>();
            services.AddSingleton<IFunctionVersionRetentionService, FunctionVersionRetentionService>();
            services.AddSingleton<IFunctionDeploymentService, FunctionDeploymentService>();
            services.AddSingleton<IFunctionRunService, FunctionRunService>();
            services.AddSingleton<IFunctionPollTokenService, FunctionPollTokenService>();
            // The shared data-plane authorizer (Common): validates a bearer token on the anonymous
            // /api/fn route exactly as it does on the proxy gateway. TryAdd inside, so this is
            // harmless alongside Proxy's and Workflow's own registration.
            services.AddEndpointAccess();
            // ctx.blocks.accessToken: a delegation grant per run, redeemed by the runner. The grant
            // store comes from Genesis (AddBlocksDelegation, part of the API and Worker setup).
            services.AddSingleton<IFunctionDelegationService, FunctionDelegationService>();
            services.AddSingleton<IFunctionInvocationService, FunctionInvocationService>();

            services.AddSingleton<IOutputActionProcessor, OutputActionProcessor>();
            services.AddSingleton<ISecretResolver>(sp => SelectSecretResolver(sp));
            services.AddHttpClient(nameof(BlocksOsHttpSecretResolver));
            // The output-action client connects only through the SSRF guard's connect callback,
            // with auto-redirect and the ambient proxy off (FunctionOutboundGuard.CreatePrimaryHandler).
            services.AddSingleton<IFunctionOutboundGuard>(_ => new FunctionOutboundGuard());
            services.AddHttpClient(nameof(OutputActionProcessor))
                .ConfigurePrimaryHttpMessageHandler(sp =>
                    FunctionOutboundGuard.CreatePrimaryHandler(sp.GetRequiredService<IFunctionOutboundGuard>()));

            services.AddValidator<CreateFunctionRequestDto, CreateFunctionRequestValidator>();
            services.AddValidator<UpdateFunctionRequestDto, UpdateFunctionRequestValidator>();
            services.AddValidator<SaveFunctionRequestDto, SaveFunctionRequestValidator>();
            services.AddValidator<TestFunctionRequestDto, TestFunctionRequestValidator>();
            services.AddValidator<DeployFunctionRequestDto, DeployFunctionRequestValidator>();

            return services;
        }

        /// <summary>Worker only: the consumers that apply results and dead letters, plus the sweeps.</summary>
        public static IServiceCollection AddFunctionsWorkerServices(this IServiceCollection services)
        {
            services.AddHostedService<FunctionResultConsumer>();
            // Output actions, decoupled from result processing so a slow endpoint never holds a
            // result entry long enough to be reclaimed and delivered twice.
            services.AddHostedService<FunctionOutputActionConsumer>();
            services.AddHostedService<FunctionBuildResultConsumer>();
            // Closes runs that will never hear back (payload expired or lost, runner died,
            // output job lost), per tenant, with conditional writes only.
            services.TryAddSingleton<IFunctionTenantSource, FunctionTenantSource>();
            services.AddHostedService<FunctionStaleRunSweeper>();
            // The background half of a delete: repeated purge passes over a tombstoned function
            // until nothing in flight can still add to it, then the document itself.
            services.AddHostedService<FunctionDeletionWorker>();
            // One-time: moves the retired FunctionRunStats counters onto the functions and drops
            // the collection. A no-op once a tenant has been through it.
            services.AddHostedService<FunctionRunStatsMigration>();
            // The other end of the runner's dead letter. Without it a job the runner could never
            // deliver leaves its record QUEUED for ever and nothing in the product says why.
            services.AddHostedService<FunctionDeadLetterConsumer>();
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
