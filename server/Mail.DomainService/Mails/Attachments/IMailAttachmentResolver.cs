namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Turns the storage file ids carried on <c>MailToBeSent.Attachments</c> into content the SMTP
    /// clients can attach.
    /// </summary>
    /// <remarks>
    /// Implementations must throw rather than return a short list. A mail that silently loses an
    /// attachment is indistinguishable from one that never had it, which is the failure mode this
    /// abstraction exists to remove.
    /// </remarks>
    public interface IMailAttachmentResolver
    {
        Task<IReadOnlyList<MailAttachment>> ResolveAsync(IEnumerable<string>? fileIds, CancellationToken cancellationToken = default);
    }
}
