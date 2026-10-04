namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// A per-method override as returned to the console. Reserved for Phase D-feature; the array on
    /// <see cref="ProxyDetailDto.MethodConfigs"/> is always empty today, but is wired so the form can
    /// round-trip it later without a contract change.
    /// </summary>
    public sealed class ProxyMethodConfigDto
    {
        public string Method { get; set; } = string.Empty;

        public List<ProxyKeyValueDto>? Headers { get; set; }

        public List<ProxyKeyValueDto>? Query { get; set; }

        public string? Upstream { get; set; }
    }
}
