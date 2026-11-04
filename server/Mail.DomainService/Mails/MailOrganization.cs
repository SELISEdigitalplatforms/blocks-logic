namespace Mail.DomainService.Mails
{
    /// <summary>
    /// The organization a mail is sent for. The Default organization's id is <c>"default"</c>; documents written
    /// before organizations existed carry no <c>OrganizationId</c> (or an empty one) and belong to it. There is no
    /// migration, so every lookup treats a missing, null or empty value as <c>"default"</c>.
    /// </summary>
    public static class MailOrganization
    {
        public const string Default = "default";

        /// <summary>The organization id itself, or <see cref="Default"/> when it is null, empty or whitespace.</summary>
        public static string Normalize(string? organizationId) =>
            string.IsNullOrWhiteSpace(organizationId) ? Default : organizationId;

        /// <summary>
        /// Whether a stored <c>OrganizationId</c> belongs to <paramref name="organizationId"/>. Stored values count as
        /// Default only when null or empty, exactly as the lookups' filters match them.
        /// </summary>
        public static bool Owns(string organizationId, string? storedOrganizationId) =>
            (string.IsNullOrEmpty(storedOrganizationId) ? Default : storedOrganizationId) == Normalize(organizationId);
    }
}
