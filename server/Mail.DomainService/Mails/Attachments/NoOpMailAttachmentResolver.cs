namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Attaches nothing. Registered where no storage driver is available, and keeps the pipeline
    /// byte-for-byte identical to how it behaved before attachment support existed.
    /// </summary>
    public sealed class NoOpMailAttachmentResolver : IMailAttachmentResolver
    {
        private static readonly IReadOnlyList<MailAttachment> None = [];

        public Task<IReadOnlyList<MailAttachment>> ResolveAsync(IEnumerable<string>? fileIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(None);
        }
    }
}
