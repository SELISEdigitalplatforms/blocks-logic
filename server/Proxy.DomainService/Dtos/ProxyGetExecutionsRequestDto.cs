namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Query string of <c>GET /api/Proxies/{proxyId}/executions</c> (SPEC &sect;3.1) &mdash; the <em>Request logs</em> list.
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

        /// <summary>
        /// Freezes the top of the window for a paging session. Echoed back on every response: send the value
        /// from the first page with each subsequent page so the offsets keep addressing the same rows.
        /// <para>
        /// Without it, page 2 is computed against a list that has grown at the head since page 1 was served,
        /// so rows repeat and rows are skipped. Absent (first page, or a filter change), the server pins it to
        /// "now"; a value in the future, or older than the 24 h window, is clamped back into range.
        /// Ignored in Live mode, which is cursor-based and wants the newest rows by definition.
        /// </para>
        /// </summary>
        public DateTime? AsOfUtc { get; set; }
    }
}
