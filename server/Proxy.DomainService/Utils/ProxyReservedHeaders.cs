namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Request headers a proxy config may not set (PS-10). They control where and how the request travels,
    /// not what it says: <c>Host</c> picks the site on a shared ingress (a public IP passes the address guard,
    /// then the name reaches an internal virtual host); the framing headers can malform or smuggle a request;
    /// the forwarding headers lie to the vendor about who is calling. Refused on save, and skipped at send time
    /// for configs saved before the rule existed.
    /// </summary>
    public static class ProxyReservedHeaders
    {
        private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            "Host",
            "Content-Length",
            "Transfer-Encoding",
            "Connection",
            "Keep-Alive",
            "Upgrade",
            "TE",
            "Trailer",
            "Expect",
            "Forwarded",
            "X-Real-IP",
        };

        private static readonly string[] Prefixes = ["Proxy-", "X-Forwarded-"];

        /// <summary>The names as shown to the user, for the validation message.</summary>
        public const string Description =
            "Host, Content-Length, Transfer-Encoding, Connection, Keep-Alive, Upgrade, TE, Trailer, Expect, "
            + "Forwarded, X-Real-IP, Proxy-* and X-Forwarded-*";

        public static bool IsReserved(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            return Names.Contains(trimmed)
                || Prefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
