using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Delegation;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using Blocks.FunctionRunner.SecretStore;
using Blocks.Secrets;
using Docker.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Utils
{
    /// <summary>
    /// Wires the runner's own services on top of the Genesis worker host.
    /// <para>
    /// Named and split the way blocks-logic names and splits its own registrations
    /// (<c>Functions.DomainService/Utils/ApplicationServiceCollectionExtensions.cs</c>:
    /// <c>AddFunctionsServices</c> for what both hosts need, <c>AddFunctionsWorkerServices</c> for
    /// the hosted services only the Worker runs) so that merging this project into that solution
    /// is a move rather than a rewrite.
    /// </para>
    /// </summary>
    public static class ApplicationServiceCollectionExtensions
    {
        /// <summary>
        /// Everything that is not a hosted service: options, the Docker and Redis clients, and the
        /// processors. Safe to call from a non-hosting process — <c>fnctl gc</c> depends on that,
        /// since it runs one sweep of the real <see cref="ImageGc"/> rather than a second
        /// implementation of "what is safe to prune".
        /// </summary>
        public static IServiceCollection AddFunctionRunnerServices(
            this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddOptions<RunnerOptions>()
                .Bind(configuration.GetSection(RunnerOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Read straight from configuration rather than from the bound options: the key names
            // are needed when the consumer groups are created at startup, which happens before
            // anything resolves IOptions<RunnerOptions>. Creating a group under the wrong name is
            // the one mistake here that leaves no trace afterwards.
            Contracts.RedisKeys.Prefix =
                configuration[$"{RunnerOptions.SectionName}:{nameof(RunnerOptions.QueuePrefix)}"] ?? string.Empty;

            // Docker over the local socket. The runner's uid is in the docker group, which is
            // root-equivalent on this host and accepted deliberately: driving the Engine is the
            // runner's whole job and nothing else runs on the VM.
            services.AddSingleton<IDockerClient>(_ =>
                new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient());

            // Redis comes from Genesis' ICacheClient so the runner uses the same connection,
            // configuration and telemetry as every other Blocks service.
            services.AddSingleton<IDatabase>(sp =>
                sp.GetRequiredService<Blocks.Genesis.ICacheClient>().CacheDatabase());

            // Admission sizes itself from the host: the signals are what it reads, the footprint
            // is what it has measured runs to cost, and the budget is the loop over the two.
            services.AddSingleton<IHostSignals, ProcHostSignals>();
            services.AddSingleton<SandboxFootprint>();
            services.AddSingleton<HostBudget>();
            // Typed client: Image GC deletes the registry's copy of an image as well as the
            // daemon's, so the two stores cannot drift apart.
            services.AddHttpClient<IRegistryClient, Maintenance.RegistryClient>();

            // Downloading a build artifact. Its own named client so the timeout suits a large,
            // slow body rather than the short calls the registry client makes.
            services.AddHttpClient(Sandbox.ArtifactImageBuilder.HttpClientName,
                client => client.Timeout = TimeSpan.FromMinutes(10));
            services.AddSingleton<Sandbox.IArtifactImageBuilder, Sandbox.ArtifactImageBuilder>();
            services.AddSingleton<Maintenance.IImageUsageLog, Maintenance.ImageUsageLog>();
            services.AddSingleton<Builds.IDependencyCache, Builds.DependencyCache>();
            services.AddSingleton<SecretStore.ISecretStoreBreaker, SecretStore.SecretStoreBreaker>();

            // Uploading a build artifact. Same reasoning as the download client: a large body
            // deserves a timeout that is not the default short one.
            services.AddHttpClient(Builds.BuildProcessor.ArtifactUploadClientName,
                client => client.Timeout = TimeSpan.FromMinutes(10));
            services.AddSingleton<StartupGuard>();
            services.AddSingleton<IImageResolver, ImageResolver>();
            services.AddSingleton<ISandbox, DockerSandbox>();
            // A build's dependency install is a sandbox too — the same gVisor boundary a run
            // gets, because `docker build` cannot be given one.
            services.AddSingleton<IDependencyInstaller, DependencyInstaller>();
            // Secret references in a run's env are resolved here, on the runner, right before the
            // sandbox starts — so the queue only ever carries {{secret.<id>}} references. The
            // store is the same SeliseBlocks.Secrets.OS domain the control plane uses: metadata on
            // the tenant's own Mongo connection (via ITenants), values in Key Vault when
            // KeyVault__KeyVaultUrl is set. Registration touches neither; the first lookup does.
            services.AddBlocksSecrets();
            services.AddSingleton<IRunSecretResolver, BlocksSecretsRunResolver>();
            // The caller's delegated access token (ctx.blocks.accessToken) is redeemed here too,
            // right before the sandbox starts, from the grant id the control plane wrote beside the
            // envelope. IDelegatedTokenProvider and the IAM endpoint come from Genesis's own
            // AddBlocksDelegation (ConfigureWorker), which already requires BLOCKS_IAM_BASE_URL.
            services.AddSingleton<IRunAccessTokenResolver, DelegatedRunAccessTokenResolver>();
            services.AddSingleton<RunProcessor>();
            services.AddSingleton<BuildProcessor>();

            // The heartbeat also produces the readiness verdict the run loop consults, so it is
            // registered as a singleton here and hosted below; both resolve the same instance.
            services.AddSingleton<HeartbeatService>();

            return services;
        }

        /// <summary>The worker host only: the loops, the reaper and the image sweeper.</summary>
        public static IServiceCollection AddFunctionRunnerWorkerServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());
            services.AddHostedService<RunConsumerService>();
            services.AddHostedService<BuildConsumerService>();
            // Test runs: build, run and delete on one host (Runs/TestConsumerService).
            services.AddHostedService<TestConsumerService>();
            services.AddHostedService<SandboxReaper>();
            services.AddHostedService<ImageGc>();

            return services;
        }
    }
}
