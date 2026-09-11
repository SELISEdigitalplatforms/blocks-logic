namespace Common.InternalService.Secret
{
    /// <summary>
    /// Query string of <c>GET /api/Secret/GetAll</c>. Every field is optional; an omitted field is not a
    /// filter. There is no paging knob on purpose &mdash; the picker wants the whole usable list at once.
    /// </summary>
    public sealed class GetSecretsRequest
    {
        /// <summary>Exact (case-insensitive) secret name. Use it to confirm one known name.</summary>
        public string? Name { get; set; }

        /// <summary>A single tag key the secret must carry.</summary>
        public string? Tag { get; set; }

        /// <summary>Case-insensitive substring match on the secret name.</summary>
        public string? Search { get; set; }
    }

    /// <summary>
    /// Response of <c>GET /api/Secret/GetAll</c> &mdash; the caller-tenant's platform secrets. A secret VALUE
    /// is never included, and neither is its Blocks Secrets type: the list is already narrowed to the
    /// platform-usable types server-side (see <see cref="Services.SecretCatalogService"/>), so the type
    /// carries no decision for a caller.
    /// </summary>
    public sealed class SecretListResponse
    {
        public List<SecretListItem> Data { get; set; } = new();

        /// <summary>Number of rows in <see cref="Data"/> (the list is never paged).</summary>
        public long TotalCount { get; set; }
    }

    /// <summary>One row of <see cref="SecretListResponse"/>: identity and tags only.</summary>
    public sealed class SecretListItem
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();
    }
}
