namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// "Who can call it" as submitted on Create / Update / Test. Everything is optional: an omitted block
    /// (or an omitted member) falls back to the safe default — a Blocks token is required and any signed-in
    /// caller may invoke. A <c>"Public"</c> kind may not carry role / permission values; the validator
    /// rejects that combination rather than silently ignoring the lists.
    /// </summary>
    public sealed class ProxyAccessInputDto
    {
        /// <summary><c>"BlocksToken"</c> (default) or <c>"Public"</c>, case-insensitive.</summary>
        public string? Kind { get; set; }

        public string? OrganizationId { get; set; }

        public ProxyAccessRuleDto? Roles { get; set; }

        public ProxyAccessRuleDto? Permissions { get; set; }

        /// <summary><c>"Or"</c> (default) or <c>"And"</c>, case-insensitive.</summary>
        public string? Combine { get; set; }
    }
}
