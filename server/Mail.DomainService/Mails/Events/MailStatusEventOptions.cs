namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Controls the status event published after every send attempt. Bound from the
    /// <c>MailStatusEvent</c> configuration section.
    /// </summary>
    public sealed class MailStatusEventOptions
    {
        public const string SectionName = "MailStatusEvent";

        /// <summary>
        /// Turn off where the broker entity does not exist. Publishing already fails soft, but an
        /// environment that will never consume the event should not pay for a failed round trip
        /// and an error log on every single mail.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Overridable so an environment can point the event at its own queue. The queue must be
        /// declared in the host's MessageConfiguration or the broker will reject the publish.
        /// </summary>
        public string QueueName { get; set; } = Utilities.CommunicationConstants.MailStatusQueueName;
    }
}
