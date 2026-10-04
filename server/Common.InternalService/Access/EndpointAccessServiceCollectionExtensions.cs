using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Common.InternalService.Access
{
    public static class EndpointAccessServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="IEndpointAccessAuthorizer"/>. Idempotent, so every module that enforces a
        /// stored access policy (Proxy, Workflow) can call it from its own registration without caring
        /// which one ran first.
        /// </summary>
        public static IServiceCollection AddEndpointAccess(this IServiceCollection services)
        {
            services.TryAddSingleton<IEndpointAccessAuthorizer, EndpointAccessAuthorizer>();
            return services;
        }
    }
}
