using Blocks.Genesis;
using Mail.DomainService.Shared.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace Mail.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class MailServerConfiguration : BaseEntity
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool EnableSSL { get; set; }
        public string SenderName { get; set; }
        public string SenderAddress { get; set; }
        public string SenderUserName { get; set; }
        public string AccountPassword { get; set; }
        public SmtpClient SmtpClient { get; set; } = SmtpClient.Default;
        public bool IsDefault { get; set; }
        public bool IsInbound { get; set; }
        public MailServiceProvider Provider { get; set; }

        /// <summary>
        /// Whether Amazon SES notification headers are added to the message.
        /// </summary>
        /// <remarks>
        /// Nullable so that "absent from the document" stays distinguishable from "explicitly
        /// false". The two are not the same here: this field has always defaulted to <c>true</c>
        /// in this service, so a record written before it existed must keep getting SES headers,
        /// while an Office 365 record must be rejected only when it explicitly asks for them.
        /// A plain <c>bool</c> collapses those cases and would fail every Office 365 record whose
        /// document happens to omit the field. Read it through
        /// <see cref="MailServerConfigurationExtensions.SendsSnsHeaders"/> rather than directly.
        /// </remarks>
        public bool? IsEnableSnsConfiguration { get; set; }

        // The fields below are additive and owned by the blocks-os configuration API. Documents
        // written before they existed deserialize with AuthenticationType = Password and
        // SecurityMode = Legacy — the zero values — and null OAuth fields. No migration.

        public MailAuthenticationType AuthenticationType { get; set; }

        public MailSecurityMode SecurityMode { get; set; }

        /// <summary>The Microsoft Entra tenant id, not the Blocks tenant id.</summary>
        public string? TenantId { get; set; }

        public string? ClientId { get; set; }

        /// <summary>
        /// The Blocks Secrets id of the OAuth client secret. An opaque reference — the plaintext
        /// is never stored here and is resolved at send time.
        /// </summary>
        public string? ClientSecretReference { get; set; }

        public string? MailboxAddress { get; set; }
    }

    public static class MailServerConfigurationExtensions
    {
        /// <summary>
        /// Whether SES notification headers should be attached. Absent means yes, preserving the
        /// default this field has always carried in this service.
        /// </summary>
        public static bool SendsSnsHeaders(this MailServerConfiguration configuration) =>
            configuration.IsEnableSnsConfiguration ?? true;

        /// <summary>
        /// Whether the record explicitly asked for SES notification headers. Absent means no, so a
        /// provider that forbids them rejects only a deliberate request rather than every record
        /// whose document predates the field.
        /// </summary>
        public static bool RequestsSnsHeaders(this MailServerConfiguration configuration) =>
            configuration.IsEnableSnsConfiguration == true;
    }

    public enum SmtpClient
    {
        Default = 0,
        MsGraph,
        MsMailKit
    }
}
