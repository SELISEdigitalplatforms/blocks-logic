namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Builds the stable <c>code</c> string for a Blocks-generated data-plane error body
    /// (<c>{ code, message, instance }</c>, SPEC &sect;3.4). The code is
    /// <c>PROXY_GATEWAY_&lt;OUTCOME&gt;</c> with the <see cref="Entities.ProxyExecutionOutcome"/> value
    /// upper-cased, e.g. <c>PROXY_GATEWAY_METHODNOTALLOWED</c>.
    /// </summary>
    public static class ProxyGatewayErrorCodes
    {
        public const string Prefix = "PROXY_GATEWAY_";

        public static string ForOutcome(string outcome) =>
            Prefix + (outcome ?? string.Empty).ToUpperInvariant();
    }
}
