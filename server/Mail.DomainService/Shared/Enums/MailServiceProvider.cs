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
        /// Exchange Online. Outbound through Microsoft Graph with OAuth client credentials, or SMTP
        /// (STARTTLS) with a mailbox password; inbound IMAP (implicit TLS) with OAuth client
        /// credentials only. The name predates Graph and inbound support and is kept because the
        /// value is persisted; the record shape is the same for both outbound transports.
        /// </summary>
        Office365Smtp = 2,

        /// <summary>
        /// Gmail / Google Workspace. Outbound SMTP (STARTTLS) and inbound IMAP (implicit TLS) with
        /// the account address and an App Password.
        /// </summary>
        Gmail = 3,
    }
}
