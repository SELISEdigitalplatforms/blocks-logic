namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// A per-method override as submitted by the console on Create / Update. Reserved for Phase D-feature;
    /// the validator rejects any non-empty <c>MethodConfigs</c> value for now.
    /// </summary>
    public sealed class ProxyMethodConfigInputDto
    {
        public string? Method { get; set; }

        public List<ProxyKeyValueInputDto>? Headers { get; set; }

        public List<ProxyKeyValueInputDto>? Query { get; set; }

        public string? Upstream { get; set; }
    }
}
