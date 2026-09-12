namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>GET /api/Proxies/{proxyId}/executions/{executionId}</c> (SPEC &sect;3.2) &mdash; one expanded row.</summary>
    public sealed class ProxyGetExecutionRequestDto
    {
        /// <summary>Required. The execution row's <see cref="Entities.ProxyExecutionEntity.ItemId"/>.</summary>
        public string? ItemId { get; set; }

        /// <summary>Required. Must match the row's <c>ProxyId</c> or the result is <c>data: null</c> (C3).</summary>
        public string? ProxyId { get; set; }
    }
}
