using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The shape an Office 365 record must have before anything is read or connected.
    /// </summary>
    /// <remarks>
    /// Checked at use time even though the owning configuration API validates on write. A record
    /// can reach here from a seed, a CLI, a restored backup or a direct database edit, and every
    /// one of the checks below guards something that would otherwise be attempted against a real
    /// vault or a real socket with wrong values.
    /// </remarks>
    public static class Office365ConfigurationContract
    {
        public const string SmtpHost = "smtp.office365.com";
        public const int SmtpPort = 587;
        public const string ImapHost = "outlook.office365.com";
        public const int ImapPort = 993;

        /// <summary>
        /// Returns the reason an outbound record cannot be used, or null when it is valid.
        /// </summary>
        /// <remarks>
        /// The reason names the offending field only. It is logged, never returned to a caller,
        /// and deliberately carries no value from the record.
        /// </remarks>
        public static string? Validate(MailServerConfiguration configuration, string? blocksTenantId)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var common = ValidateCommon(configuration, blocksTenantId);
            if (common is not null)
            {
                return common;
            }

            if (configuration.IsInbound)
            {
                return "configuration is inbound";
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

            return configuration.AuthenticationType switch
            {
                MailAuthenticationType.OAuthClientCredentials => ValidateOAuth(configuration),
                MailAuthenticationType.Password => ValidatePassword(configuration),
                _ => "authentication type is not supported"
            };
        }

        /// <summary>
        /// Returns the reason an inbound record cannot be used, or null when it is valid.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OAuth only: inbound reads through Graph with a client-credentials token, so a password
        /// record is refused here rather than failing on every poll.
        /// </para>
        /// <para>
        /// The IMAP host, port and security mode are still required although Graph uses none of
        /// them — the same arrangement as an outbound OAuth record, which keeps its SMTP endpoint.
        /// blocks-os normalizes every inbound record to exactly these values, so they are the
        /// stored shape of a well-formed record, and anything else is one that did not come
        /// through the configuration API.
        /// </para>
        /// </remarks>
        public static string? ValidateInbound(MailServerConfiguration configuration, string? blocksTenantId)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var common = ValidateCommon(configuration, blocksTenantId);
            if (common is not null)
            {
                return common;
            }

            if (!configuration.IsInbound)
            {
                return "configuration is outbound";
            }

            if (configuration.AuthenticationType != MailAuthenticationType.OAuthClientCredentials)
            {
                return "authentication type is not OAuthClientCredentials";
            }

            if (configuration.SecurityMode != MailSecurityMode.SslOnConnect)
            {
                return "security mode is not SslOnConnect";
            }

            if (!string.Equals(configuration.Host, ImapHost, StringComparison.OrdinalIgnoreCase))
            {
                return "host is not the Office 365 IMAP endpoint";
            }

            if (configuration.Port != ImapPort)
            {
                return "port is not 993";
            }

            return ValidateOAuth(configuration);
        }

        private static string? ValidateCommon(MailServerConfiguration configuration, string? blocksTenantId)
        {
            if (string.IsNullOrWhiteSpace(blocksTenantId))
            {
                return "ambient tenant context is missing";
            }

            if (configuration.Provider != MailServiceProvider.Office365Smtp)
            {
                return "provider is not Office365Smtp";
            }

            // Only an explicit request is refused. A record whose document simply predates the
            // field must not be read as asking for SES headers, or every such record would fail
            // closed for a reason nobody chose.
            if (configuration.RequestsSnsHeaders())
            {
                return "SNS configuration is enabled";
            }

            return null;
        }

        private static string? ValidateOAuth(MailServerConfiguration configuration)
        {
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

        private static string? ValidatePassword(MailServerConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration.SenderUserName))
            {
                return "username is missing";
            }

            if (string.IsNullOrEmpty(configuration.AccountPassword))
            {
                return "password is missing";
            }

            return null;
        }
    }
}
