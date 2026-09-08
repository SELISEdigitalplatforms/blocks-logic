namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// The request-log status filter (SPEC &sect;3): <c>all</c> (no filter), <c>2xx</c> (200&ndash;299),
    /// <c>4xx</c> (400&ndash;499), <c>5xx</c> (500&ndash;599). The console never sends <c>3xx</c>; anything
    /// outside this set is a <c>PROXY_VALIDATION</c> failure (C1).
    /// </summary>
    public enum ProxyStatusClass
    {
        All,
        TwoXx,
        FourXx,
        FiveXx,
    }

    /// <summary>Parsing and messaging for the <c>statusClass</c> query/body field.</summary>
    public static class ProxyStatusClassParser
    {
        /// <summary>The allowed literals, in the order the error message lists them.</summary>
        public const string AllowedList = "all, 2xx, 4xx, 5xx";

        /// <summary>The message returned in the errors map when <c>statusClass</c> is not recognised (C1).</summary>
        public const string InvalidMessage = "Must be one of all, 2xx, 4xx, 5xx.";

        /// <summary>
        /// Maps a caller value to a <see cref="ProxyStatusClass"/>. A <c>null</c>/empty value defaults to
        /// <see cref="ProxyStatusClass.All"/>. Returns <c>false</c> (and leaves <paramref name="result"/> at
        /// <see cref="ProxyStatusClass.All"/>) for any other value so the caller can raise C1.
        /// </summary>
        public static bool TryParse(string? value, out ProxyStatusClass result)
        {
            result = ProxyStatusClass.All;
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "all":
                    result = ProxyStatusClass.All;
                    return true;
                case "2xx":
                    result = ProxyStatusClass.TwoXx;
                    return true;
                case "4xx":
                    result = ProxyStatusClass.FourXx;
                    return true;
                case "5xx":
                    result = ProxyStatusClass.FiveXx;
                    return true;
                default:
                    return false;
            }
        }
    }
}
