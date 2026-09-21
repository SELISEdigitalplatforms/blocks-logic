using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The shape an Office 365 record must have before anything is read or connected.
    /// </summary>
    /// <remarks>
    /// Checked at send time even though the owning configuration API validates on write. A record
    /// can reach here from a seed, a CLI, a restored backup or a direct database edit, and every
    /// one of the checks below guards something that would otherwise be attempted against a real
    /// vault or a real socket with wrong values.
    /// </remarks>
    public static class Office365ConfigurationContract
    {
        public const string SmtpHost = "smtp.office365.com";
        public const int SmtpPort = 587;

        /// <summary>
        /// Returns the reason the record cannot be used, or null when it is valid.
        /// </summary>
        /// <remarks>
        /// The reason names the offending field only. It is logged, never returned to a caller,
        /// and deliberately carries no value from the record.
        /// </remarks>
        public static string? Validate(MailServerConfiguration configuration, string? blocksTenantId)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (string.IsNullOrWhiteSpace(blocksTenantId))
            {
                return "ambient tenant context is missing";
            }

            if (configuration.Provider != MailServiceProvider.Office365Smtp)
            {
                return "provider is not Office365Smtp";
            }

            if (configuration.IsInbound)
            {
                return "configuration is inbound";
            }

            if (configuration.AuthenticationType != MailAuthenticationType.OAuthClientCredentials)
            {
                return "authentication type is not OAuthClientCredentials";
            }

            if (configuration.SecurityMode != MailSecurityMode.StartTls)
            {
                return "security mode is not StartTls";
            }

            if (!string.Equals(configuration.Host, SmtpHost, StringComparison.OrdinalIgnoreCase))
            {
                return "host is not the Office 365 SMTP endpoint";
            }

            if (configuration.Port != SmtpPort)
            {
                return "port is not 587";
            }

            // Only an explicit request is refused. A record whose document simply predates the
            // field must not be read as asking for SES headers, or every such record would fail
            // closed for a reason nobody chose.
            if (configuration.RequestsSnsHeaders())
            {
                return "SNS configuration is enabled";
            }

            if (string.IsNullOrWhiteSpace(configuration.TenantId))
            {
                return "tenant id is missing";
            }

            if (string.IsNullOrWhiteSpace(configuration.ClientId))
            {
                return "client id is missing";
            }

            if (string.IsNullOrWhiteSpace(configuration.ClientSecretReference))
            {
                return "client secret reference is missing";
            }

            if (string.IsNullOrWhiteSpace(configuration.MailboxAddress))
            {
                return "mailbox address is missing";
            }

            return null;
        }
    }
}
