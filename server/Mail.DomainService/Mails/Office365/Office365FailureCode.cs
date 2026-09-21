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

        /// <summary>STARTTLS negotiation, certificate validation or transport setup failed.</summary>
        public const string TlsFailed = "O365_TLS_FAILED";

        /// <summary>SMTP XOAUTH2 authentication was rejected.</summary>
        public const string AuthenticationFailed = "O365_AUTHENTICATION_FAILED";

        /// <summary>Exchange refused the configured From identity.</summary>
        public const string SendAsDenied = "O365_SEND_AS_DENIED";

        /// <summary>Exchange returned a throttling or transient rate response.</summary>
        public const string Throttled = "O365_THROTTLED";

        /// <summary>One or more recipients were rejected.</summary>
        public const string RecipientRejected = "O365_RECIPIENT_REJECTED";

        /// <summary>Any other SMTP or session failure.</summary>
        public const string SmtpFailed = "O365_SMTP_FAILED";
    }
}
