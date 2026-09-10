namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Body of <c>POST /api/Proxy/GetExecutions</c> (SPEC &sect;3.1) &mdash; the <em>Request logs</em> list.
    /// Only rows from the rolling last 24 h are ever returned.
    /// </summary>
    public sealed class ProxyGetExecutionsRequestDto
    {
        /// <summary>Required. The <see cref="Entities.ProxyDetailEntity.ItemId"/> whose executions to list.</summary>
        public string? ProxyId { get; set; }

        /// <summary><c>all</c> (default) | <c>2xx</c> | <c>4xx</c> | <c>5xx</c>. Anything else is a 400 (C1).</summary>
        public string? StatusClass { get; set; }

        /// <summary>
        /// Live mode: when set, return only rows STRICTLY NEWER than this execution id (by
        /// <c>StartedAtUtc</c>, then <c>ItemId</c>). An unknown/out-of-window id is ignored (C8).
        /// </summary>
        public string? AfterId { get; set; }

        /// <summary>1..200, default 25.</summary>
        public int PageSize { get; set; } = 25;

        /// <summary>Zero-based, default 0. Ignored when <see cref="AfterId"/> is supplied.</summary>
        public int PageNumber { get; set; }
    }
}
