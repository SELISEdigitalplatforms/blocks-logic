namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// "Who can call it" as returned to the console. Always fully populated: <see cref="Kind"/> is
    /// <c>"BlocksToken"</c> or <c>"Public"</c>, <see cref="Combine"/> is <c>"Or"</c> or <c>"And"</c>, and the
    /// two rules are present (possibly with empty value lists) so the form never has to null-check.
    /// </summary>
    public sealed class ProxyAccessDto
    {
        public string Kind { get; set; } = "BlocksToken";

        /// <summary>Organization the caller must belong to; empty ⇒ no restriction. Not edited by the proxy form today.</summary>
        public string OrganizationId { get; set; } = string.Empty;

        public ProxyAccessRuleDto Roles { get; set; } = new() { Mode = "any", Values = new List<string>() };

        public ProxyAccessRuleDto Permissions { get; set; } = new() { Mode = "any", Values = new List<string>() };

        /// <summary>How the roles and permissions rules combine when both are configured.</summary>
        public string Combine { get; set; } = "Or";
    }
}
