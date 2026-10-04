namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// One "Restrict further" list as it travels in both directions: <c>{ mode: "any" | "all", values }</c>.
    /// An empty <see cref="Values"/> list is "no restriction". Used for roles (slugs) and permissions
    /// (resource keys) alike.
    /// </summary>
    public sealed class ProxyAccessRuleDto
    {
        /// <summary><c>"any"</c> (default) ⇒ the caller needs at least one value; <c>"all"</c> ⇒ every value.</summary>
        public string? Mode { get; set; }

        public List<string>? Values { get; set; }
    }
}
