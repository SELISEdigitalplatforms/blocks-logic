namespace Mail.DomainService.Shared.Enums
{
    /// <summary>
    /// Outbound mail provider a tenant is configured to use.
    /// Numeric values match blocks-os Configuration.DomainService.Shared.Enums.MailServiceProvider.
    /// </summary>
    public enum MailServiceProvider
    {
        /// <summary>Amazon Simple Email Service.</summary>
        AmazonSes = 0,

        /// <summary>Zoho Mail transactional API.</summary>
        Zoho = 1,
    }
}
