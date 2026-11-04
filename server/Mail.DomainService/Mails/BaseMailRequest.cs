namespace Mail.DomainService.Mails
{
    public class BaseMailRequest
    {
        public Dictionary<string, string> SubjectDataContext { get; set; } = new Dictionary<string, string>();
        public IEnumerable<string> To { get; set; }
        public IEnumerable<string> Bcc { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Cc { get; set; } = Enumerable.Empty<string>();
        // Optional at the HTTP boundary: [ApiController] would turn a non-nullable string into an implicit [Required]
        // and refuse a test mail by templateId (which carries neither) before the action runs. A missing value on any
        // other send is reported by EmailValidator inside the usual BaseMutationResponse.
        public string? Purpose { get; set; }
        public string? Language { get; set; }
        public IEnumerable<string> ReplyTo { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Attachments { get; set; } = Enumerable.Empty<string>();
        public Dictionary<string, string> BodyDataContext { get; set; } = new Dictionary<string, string>();
        public bool SendPhoneNumberAsEmail { get; set; } = false;

        /// <summary>
        /// Opaque value echoed back on the MailSentEvent so the caller can match the outcome to
        /// its own work. Optional; existing payloads are unaffected.
        /// </summary>
        public string? CorrelationId { get; set; }
    }
}
