using Blocks.Genesis;

namespace Mail.DomainService.Dtos
{
    /// <summary>
    /// Published after every send attempt so the service that asked for the mail can learn what
    /// happened. Mail send is server to server and the queue entry points are fire-and-forget, so
    /// without this the caller has no way to find out.
    /// </summary>
    /// <remarks>
    /// This reports the outcome of handing the message to the SMTP server. Delivery, bounce and
    /// complaint arrive later and out of band, and land on <c>MailBoxEntity</c>.
    /// </remarks>
    public class MailSentEvent : IProjectKey
    {
        /// <summary>Id of the persisted mail. The key to every log line about this send.</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Whatever the caller put on the request, echoed back so it can match this to its own work.</summary>
        public string? CorrelationId { get; set; }

        public bool IsSuccess { get; set; }

        /// <summary>Why the send failed. Null on success.</summary>
        public string? Error { get; set; }

        public string? Purpose { get; set; }

        public string? Language { get; set; }

        public IEnumerable<string> To { get; set; } = [];

        public int AttachmentCount { get; set; }

        public DateTime SentOnUtc { get; set; }

        public string? ProjectKey { get; set; }
    }
}
