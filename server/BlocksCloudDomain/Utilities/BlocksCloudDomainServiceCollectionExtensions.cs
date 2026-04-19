using BlocksCloudDomain.Repositories;
using BlocksCloudDomain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlocksCloudDomain.Utilities
{
    public static class BlocksCloudDomainServiceCollectionExtensions
    {
        public static void AddBlocksCloudDomainServices(this IServiceCollection services)
        {
            services.AddSingleton<IApiEndpointConfigRepository, ApiEndpointConfigRepository>();
            services.AddSingleton<IApiEndpointConfigService, ApiEndpointConfigService>();
        }
    }
}
