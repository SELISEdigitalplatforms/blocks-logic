using System.Text.Json.Serialization;

namespace DomainService.Workflow.Nodes.TriggerEmailV1
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MailStatus
    {
        Sent,
        Delivered,
        Bounced,
        Complained,
        Rejected,
        Received,
        Unknown
    }
    public class EmailBox
    {

        public string ItemId { get; set; }
        public string MessageId { get; set; }
        public string? MailServerConfigurationId { get; set; } = String.Empty;
        public string Subject { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Body { get; set; }

        public MailStatus Status { get; set; }
        public string Error { get; set; }
        public DateTime Date { get; set; }
        public string RawMime { get; set; }
        public bool IsInbound { get; set; }
    }
}