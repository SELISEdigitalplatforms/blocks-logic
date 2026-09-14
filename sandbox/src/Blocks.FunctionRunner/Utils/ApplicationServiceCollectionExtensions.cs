using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
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

            // Docker over the local socket. The runner's uid is in the docker group, which is
            // root-equivalent on this host and accepted deliberately: driving the Engine is the
            // runner's whole job and nothing else runs on the VM.
            services.AddSingleton<IDockerClient>(_ =>
                new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient());

            // Redis comes from Genesis' ICacheClient so the runner uses the same connection,
            // configuration and telemetry as every other Blocks service.
            services.AddSingleton<IDatabase>(sp =>
                sp.GetRequiredService<Blocks.Genesis.ICacheClient>().CacheDatabase());

            services.AddSingleton<HostBudget>();
            // Typed client: Image GC deletes the registry's copy of an image as well as the
            // daemon's, so the two stores cannot drift apart.
            services.AddHttpClient<IRegistryClient, Maintenance.RegistryClient>();
            services.AddSingleton<StartupGuard>();
            services.AddSingleton<IImageResolver, ImageResolver>();
            services.AddSingleton<ISandbox, DockerSandbox>();
            // A build's dependency install is a sandbox too — the same gVisor boundary a run
            // gets, because `docker build` cannot be given one.
            services.AddSingleton<IDependencyInstaller, DependencyInstaller>();
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
            services.AddHostedService<SandboxReaper>();
            services.AddHostedService<ImageGc>();

            return services;
        }
    }
}
