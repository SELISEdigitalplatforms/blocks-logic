namespace Mail.DomainService.Mails
{
    public class MailBody
    {
        public string Body { get; set; }
        public string Subject { get; set; }

        /// <summary>
        /// Attachments already resolved to content. Empty unless an <see cref="IMailAttachmentResolver"/>
        /// produced something, which keeps every existing caller's MIME output unchanged.
        /// </summary>
        public IReadOnlyList<MailAttachment> Attachments { get; set; } = [];
    }
}
