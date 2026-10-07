namespace Proxy.DomainService.Dtos
{
    /// <summary>Body of <c>PATCH /api/Proxies/{proxyId}</c>.</summary>
    public sealed class ProxyToggleRequestDto
    {
        public string? ItemId { get; set; }

        public bool Enabled { get; set; }

        /// <summary>
        /// The <c>currentVersion</c> the console loaded (PX-16). When the proxy has moved on since, the write is
        /// refused with 409 <c>PROXY_VERSION_CONFLICT</c> instead of silently overwriting the other change.
        /// <c>null</c> (old clients) ⇒ only the read-then-write race on the server is guarded.
        /// </summary>
        public int? ExpectedVersion { get; set; }
    }
}
