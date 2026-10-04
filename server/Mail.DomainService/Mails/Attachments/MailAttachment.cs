namespace Mail.DomainService.Mails
{
    /// <summary>
    /// An attachment that has been resolved to content: the bytes, plus what a mail client needs
    /// to render them. Produced by <see cref="IMailAttachmentResolver"/> and carried to the SMTP
    /// clients on <see cref="MailBody.Attachments"/>.
    /// </summary>
    public sealed class MailAttachment
    {
        public string FileName { get; init; } = string.Empty;

        public string ContentType { get; init; } = DefaultContentType;

        public byte[] Content { get; init; } = [];

        public const string DefaultContentType = "application/octet-stream";
    }
}
