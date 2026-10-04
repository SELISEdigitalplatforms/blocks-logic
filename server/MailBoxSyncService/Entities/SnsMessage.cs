namespace MailBoxSyncService.Entities
{
    public class SesMail
    {
        public string MessageId { get; set; }
        public string Source { get; set; }
        public List<string> Destination { get; set; }
        public List<SesHeader> Headers { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SesEventNotification
    {
        public string EventType { get; set; }
        public SesMail Mail { get; set; }
        public SesDelivery Delivery { get; set; }
        public SesBounce Bounce { get; set; }
        public SesComplaint Complaint { get; set; }
    }

    public class SesDelivery
    {
        public DateTime Timestamp { get; set; }
        public decimal ProcessingTimeMillis { get; set; }
        public List<string> Recipients { get; set; }
    }

    public class SesBounce
    {
        public string BounceType { get; set; }
        public string BounceSubType { get; set; }
        public List<BouncedRecipient> BouncedRecipients { get; set; }
    }

    public class BouncedRecipient
    {
        public string EmailAddress { get; set; }
        public string DiagnosticCode { get; set; }
    }

    public class SesComplaint
    {
        public DateTime Timestamp { get; set; }
        public List<ComplainedRecipient> ComplainedRecipients { get; set; }
    }

    public class ComplainedRecipient
    {
        public string EmailAddress { get; set; }
    }

    public class SnsMessage
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string SubscribeURL { get; set; }
        public List<SesHeader> sesHeaders { get; set; }
    }

    public class SesHeader
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
