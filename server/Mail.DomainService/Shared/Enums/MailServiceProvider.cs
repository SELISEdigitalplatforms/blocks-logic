namespace Mail.DomainService.Shared.Enums
{
    /// <summary>
    /// The mail integration a tenant is configured to use.
    /// Numeric values match blocks-os Configuration.DomainService.Shared.Enums.MailServiceProvider.
    /// </summary>
    /// <remarks>
    /// An id identifies a concrete integration and transport contract, not merely a vendor brand.
    /// Existing values are never reordered or repurposed: they are persisted in Mongo documents
    /// and mirrored by blocks-os and blocks-cli.
    /// </remarks>
    public enum MailServiceProvider
    {
        /// <summary>Amazon Simple Email Service.</summary>
        AmazonSes = 0,

        /// <summary>Zoho Mail transactional API.</summary>
        Zoho = 1,

        /// <summary>
        /// Exchange Online SMTP with OAuth client credentials (STARTTLS + SASL XOAUTH2).
        /// Outbound only.
        /// </summary>
        Office365Smtp = 2,
    }
}
