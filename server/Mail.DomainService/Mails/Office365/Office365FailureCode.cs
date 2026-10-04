namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The classified reason an Office 365 send failed. Log-only.
    /// </summary>
    /// <remarks>
    /// Callers keep the unchanged boolean result and the existing generic status error; this is
    /// what an operator reads instead. Every code is safe to emit: none of them is derived from,
    /// or carries, a secret, a token, a recipient or a raw provider response.
    /// </remarks>
    public static class Office365FailureCode
    {
        /// <summary>Runtime configuration or context validation failed.</summary>
        public const string ConfigInvalid = "O365_CONFIG_INVALID";

        /// <summary>The secret is missing, denied, locked or deleted.</summary>
        public const string SecretResolutionFailed = "O365_SECRET_RESOLUTION_FAILED";

        /// <summary>
        /// The secret store could not be reached.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="SecretResolutionFailed"/> on purpose. The two need opposite
        /// responses — a missing or denied secret is a broken configuration that will never
        /// succeed, an unreachable store is transient — and an operator should not have to read an
        /// exception type to tell them apart.
        /// </remarks>
        public const string VaultUnavailable = "O365_VAULT_UNAVAILABLE";

        /// <summary>The Azure credential or token request failed.</summary>
        public const string TokenAcquisitionFailed = "O365_TOKEN_ACQUISITION_FAILED";

        /// <summary>TLS negotiation, certificate validation or transport setup failed.</summary>
        public const string TlsFailed = "O365_TLS_FAILED";

        /// <summary>
        /// The credential was refused: SMTP password authentication, or a Graph 401 for the token.
        /// </summary>
        public const string AuthenticationFailed = "O365_AUTHENTICATION_FAILED";

        /// <summary>
        /// Graph accepted the token but the application may not do this with the mailbox.
        /// </summary>
        /// <remarks>
        /// <c>Mail.Send</c> not granted or not consented, an application access policy that
        /// excludes the mailbox, or — for a message too large to send in one request —
        /// <c>Mail.ReadWrite</c> missing for the draft it has to be built in. Inbound, it means
        /// <c>Mail.Read</c> is not granted or the access policy excludes the mailbox.
        /// </remarks>
        public const string PermissionDenied = "O365_PERMISSION_DENIED";

        /// <summary>The configured mailbox does not exist, or has no Exchange Online mailbox.</summary>
        public const string MailboxNotFound = "O365_MAILBOX_NOT_FOUND";

        /// <summary>Exchange refused the configured From identity.</summary>
        public const string SendAsDenied = "O365_SEND_AS_DENIED";

        /// <summary>Exchange returned a throttling or transient rate response.</summary>
        public const string Throttled = "O365_THROTTLED";

        /// <summary>One or more recipients were rejected, or could not be parsed.</summary>
        public const string RecipientRejected = "O365_RECIPIENT_REJECTED";

        /// <summary>The message is over the mailbox's or the organization's size limit.</summary>
        public const string MessageTooLarge = "O365_MESSAGE_TOO_LARGE";

        /// <summary>Any other SMTP or session failure.</summary>
        public const string SmtpFailed = "O365_SMTP_FAILED";

        /// <summary>Any other Graph failure, including a request that never got a response.</summary>
        public const string GraphFailed = "O365_GRAPH_FAILED";
    }
}
