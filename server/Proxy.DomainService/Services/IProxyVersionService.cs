using Proxy.DomainService.Dtos;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Read and revert access to a proxy's append-only change history. History survives deletion of the
    /// proxy; revert requires the proxy to still exist.
    /// </summary>
    public interface IProxyVersionService
    {
        Task<ProxyGetVersionsResponseDto> GetVersionsAsync(string tenantId, ProxyGetVersionsRequestDto request);

        Task<ProxyMutationResponse> RevertAsync(string tenantId, ProxyRevertRequestDto request);
    }
}
