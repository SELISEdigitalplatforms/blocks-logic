namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Raised when an attachment cannot be resolved. Thrown rather than swallowed on purpose: a
    /// mail delivered without its attachment looks identical to one that never had it, and that
    /// ambiguity is what made the original defect so hard to diagnose.
    /// </summary>
    public sealed class MailAttachmentException : Exception
    {
        public MailAttachmentException()
        {
        }

        public MailAttachmentException(string message) : base(message)
        {
        }

        public MailAttachmentException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
