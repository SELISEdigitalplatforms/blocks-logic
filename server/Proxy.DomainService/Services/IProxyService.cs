using Proxy.DomainService.Dtos;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Proxy control plane: create, read, update, delete, and enable / disable. Every configuration
    /// mutation is snapshotted as a <c>ProxyVersionEntity</c>. All operations are scoped to the caller's tenant.
    /// </summary>
    public interface IProxyService
    {
        Task<ProxyGetAllResponseDto> GetAllAsync(string tenantId, ProxyGetAllRequestDto request);

        Task<ProxyGetResponseDto> GetAsync(string tenantId, ProxyGetRequestDto request);

        Task<ProxyMutationResponse> CreateAsync(string tenantId, ProxyCreateRequestDto request);

        Task<ProxyMutationResponse> UpdateAsync(string tenantId, ProxyUpdateRequestDto request);

        Task<ProxyMutationResponse> ToggleAsync(string tenantId, ProxyToggleRequestDto request);

        Task<ProxyMutationResponse> DeleteAsync(string tenantId, ProxyDeleteRequestDto request);
    }
}
