using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace CloudConfiguration.DomainService.Mail.Entities
{
    /// <summary>
    /// The Cloud Configuration view of a mail configuration.
    /// </summary>
    /// <remarks>
    /// Carries the additive OAuth and transport fields even though nothing here reads them.
    /// <c>SaveMailConfigurationAsync</c> writes with <c>ReplaceOneAsync</c>, which replaces the
    /// whole document — so a field missing from this type is a field deleted from the record. For
    /// an Office 365 configuration that would drop <see cref="ClientSecretReference"/> and strand
    /// the secret it points at. Round-tripping them keeps this path from destroying what it does
    /// not understand.
    /// <para>
    /// Creating an Office 365 configuration through this path is still not supported: the secret
    /// lifecycle belongs to the owning blocks-os API, and this service is not a second
    /// credential-writing authority.
    /// </para>
    /// </remarks>
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
        public bool UseDefaultCredentials { get; set; }
        public SmtpClient SmtpClient { get; set; } = SmtpClient.Default;
        public bool IsDefault { get; set; }
        public bool IsInbound { get; set; }
        public MailServiceProvider Provider { get; set; }

        public bool? IsEnableSnsConfiguration { get; set; }

        public int AuthenticationType { get; set; }

        public int SecurityMode { get; set; }

        public string? TenantId { get; set; }

        public string? ClientId { get; set; }

        public string? ClientSecretReference { get; set; }

        public string? MailboxAddress { get; set; }
    }

    public enum SmtpClient
    {
        Default = 0,
        MsGraph,
        MsMailKit
    }
}
