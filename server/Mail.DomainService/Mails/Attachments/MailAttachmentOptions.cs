namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Limits and identity for attachment resolution. Bound from the <c>MailAttachments</c>
    /// configuration section; the defaults are safe to run with unchanged.
    /// </summary>
    public sealed class MailAttachmentOptions
    {
        public const string SectionName = "MailAttachments";

        /// <summary>
        /// Principal the resolver downloads as. Mail is sent server to server, so there is no
        /// end-user identity to authorise against; storage access is default-deny, which means
        /// this principal needs an explicit Download grant on the files mail is allowed to attach.
        /// </summary>
        public string SystemUserId { get; set; } = "blocks-mail-service";

        /// <summary>
        /// Resolve as the caller when the request carries a real user, falling back to
        /// <see cref="SystemUserId"/> only when it does not (worker, anonymous send).
        /// A caller allowed to request the mail may attach what they can already download, and
        /// this avoids needing a blanket grant for the service principal on every file.
        /// </summary>
        public bool PreferCallerIdentity { get; set; } = true;

        public int MaxAttachmentCount { get; set; } = 10;

        /// <summary>Total resolved bytes across all attachments. Base64 adds roughly a third on the wire.</summary>
        public long MaxTotalBytes { get; set; } = 10 * 1024 * 1024;

        public int DownloadTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Rejects a send whose attachment ids do not exist, instead of sending without them.
        /// Turn off for one release if existing callers are known to pass stale ids.
        /// </summary>
        public bool ValidateAttachmentsExist { get; set; } = true;
    }
}
