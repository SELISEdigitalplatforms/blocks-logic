namespace Proxy.DomainService.Dtos
{
    /// <summary>Raw <c>{ key, value, isSecretRef }</c> pair as submitted by the console on Create / Update.</summary>
    public sealed class ProxyKeyValueInputDto
    {
        public string? Key { get; set; }

        public string? Value { get; set; }

        /// <summary>
        /// The console's "Vault" checkbox for this row. When <c>true</c> the pair is stored and forwarded as a
        /// secret reference even if the value is not itself a <c>${SECRET.NAME}</c> token. A <c>${SECRET.NAME}</c>
        /// value is always treated as a secret reference regardless of this flag.
        /// </summary>
        public bool? IsSecretRef { get; set; }
    }
}
