using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Read-only mailbox picker fields for workflow Email Trigger. Does not inherit BaseEntity
    /// so secrets and audit fields are never serialized.
    /// </summary>
    public class MailServerConfigurationSummary
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsInbound { get; set; }
        public MailServiceProvider Provider { get; set; }
    }
}
