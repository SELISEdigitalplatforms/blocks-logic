namespace Proxy.DomainService.Entities
{
    /// <summary>How the gateway treats the upstream response body before relaying it.</summary>
    public enum ProxyResponseMode
    {
        /// <summary>Relay the body byte-for-byte (default; today's behaviour).</summary>
        All,

        /// <summary>Project the body to the configured <c>ResponseInclude</c> paths (see ProxyResponseProjector).</summary>
        Select,
    }
}
