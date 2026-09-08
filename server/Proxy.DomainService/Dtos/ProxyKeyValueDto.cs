namespace Proxy.DomainService.Dtos
{
    /// <summary>A header / query pair as returned to the console, including the computed secret-reference flag.</summary>
    public sealed class ProxyKeyValueDto
    {
        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public bool IsSecretRef { get; set; }
    }
}
