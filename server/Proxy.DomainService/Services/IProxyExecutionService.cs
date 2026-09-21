using Proxy.DomainService.Dtos;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Read-only projections over the <c>ProxyExecutions</c> rows Phase 2 writes: the <em>Request logs</em>
    /// list and expanded row, the Overview tiles, and the CSV export (SPEC3 &sect;3). Nothing here writes.
    /// All "24 h" figures use a rolling window computed per request from an injected clock.
    /// </summary>
    public interface IProxyExecutionService
    {
        /// <summary>SPEC &sect;3.1 &mdash; filtered, paged, newest-first log list (or Live tail via <c>afterId</c>).</summary>
        Task<ProxyGetExecutionsResponseDto> GetExecutionsAsync(string tenantId, ProxyGetExecutionsRequestDto request);

        /// <summary>SPEC &sect;3.2 &mdash; one expanded row with the display-clipped response body; <c>data:null</c> on any mismatch.</summary>
        Task<ProxyGetExecutionResponseDto> GetExecutionAsync(string tenantId, ProxyGetExecutionRequestDto request);

        /// <summary>SPEC &sect;3.3 &mdash; the rolling 24 h tiles plus the Configuration-panel convenience fields.</summary>
        Task<ProxyGetOverviewResponseDto> GetOverviewAsync(string tenantId, ProxyGetOverviewRequestDto request);

    }

}
