namespace Proxy.DomainService.Dtos
{
    /// <summary>Query string of <c>GET /api/Proxy/Variables</c>. All fields optional.</summary>
    public sealed class ProxyVariableListRequestDto
    {
        /// <summary>Case-insensitive substring match on the variable name.</summary>
        public string? Search { get; set; }
    }

    /// <summary>
    /// Response of <c>GET /api/Proxy/Variables</c> &mdash; the caller-tenant's Blocks Secrets, names / ids /
    /// type / tags only, for the console's <c>{{$VAR.name}}</c> picker. A secret VALUE is never included;
    /// values are resolved server-side on the forward / Test path by <see cref="Services.IProxyVariableResolver"/>.
    /// </summary>
    public sealed class ProxyVariableListResponseDto
    {
        public List<ProxyVariableDto> Data { get; set; } = new();

        public long TotalCount { get; set; }
    }

    /// <summary>One row of <see cref="ProxyVariableListResponseDto"/> (a Blocks Secrets <c>SecretResult</c>, trimmed).</summary>
    public sealed class ProxyVariableDto
    {
        public string SecretId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Blocks Secrets type: <c>service</c> / <c>api</c> / <c>both</c>. The console offers only <c>service</c> / <c>both</c>.</summary>
        public string Type { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();
    }

    /// <summary>One entry of the secret-tag catalog from <c>GET /api/Proxy/VariableTags</c>.</summary>
    public sealed class ProxyVariableTagDto
    {
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;
    }
}
