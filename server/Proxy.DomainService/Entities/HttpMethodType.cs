namespace Proxy.DomainService.Entities
{
    /// <summary>The HTTP methods a proxy may accept. Persisted to Mongo as its string name.</summary>
    public enum HttpMethodType
    {
        Get,
        Post,
        Put,
        Patch,
        Delete,
    }

    /// <summary>
    /// Parsing / formatting helpers for <see cref="HttpMethodType"/>. The domain layer works in the enum;
    /// only the wire (request/response DTOs, the <c>Allow</c> header, logs) uses the upper-case string.
    /// </summary>
    public static class HttpMethodTypeExtensions
    {
        /// <summary>Every declared method, in enum order. Used by the validator.</summary>
        public static IReadOnlyList<HttpMethodType> All { get; } = new[]
        {
            HttpMethodType.Get,
            HttpMethodType.Post,
            HttpMethodType.Put,
            HttpMethodType.Patch,
            HttpMethodType.Delete,
        };

        /// <summary>Case-insensitive, whitespace-tolerant parse. Returns <c>false</c> for anything else.</summary>
        public static bool TryParse(string? raw, out HttpMethodType value)
        {
            switch ((raw ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "GET": value = HttpMethodType.Get; return true;
                case "POST": value = HttpMethodType.Post; return true;
                case "PUT": value = HttpMethodType.Put; return true;
                case "PATCH": value = HttpMethodType.Patch; return true;
                case "DELETE": value = HttpMethodType.Delete; return true;
                default: value = default; return false;
            }
        }

        /// <summary>The canonical upper-case wire name (<c>"GET"</c>, <c>"POST"</c>, ...).</summary>
        public static string Wire(this HttpMethodType method) => method.ToString().ToUpperInvariant();
    }
}
