using MongoDB.Bson;

namespace Workflow.DomainService.Services
{
    /// <summary>Webhook request formats, as saved in the trigger's <c>requestFormat</c> parameter.</summary>
    public static class WebhookRequestFormats
    {
        /// <summary>Any body type; each request is one item <c>{ body, files, contentType, headers, query }</c>.</summary>
        public const string Standard = "standard";

        /// <summary>
        /// A JSON object or an array of objects; items are the JSON itself. Also every webhook whose
        /// <c>requestFormat</c> is missing or unknown, which is every webhook saved before this setting existed.
        /// </summary>
        public const string JsonLegacy = "jsonLegacy";
    }

    /// <summary>Request limits for the Standard request format (SPEC8 § Limits).</summary>
    public static class WebhookIntakeLimits
    {
        public const long NonFileMaxBytes = 5L * 1024 * 1024;
        public const long FileRequestMaxBytes = 50L * 1024 * 1024;
        public const int MaxFileParts = 10;
        public const int MaxFileNameLength = 255;

        /// <summary>
        /// Kestrel limit applied to the webhook routes: the file limit plus room for multipart framing, so the
        /// precise 413 comes from the intake rather than from the server cutting the connection.
        /// </summary>
        public const long RequestSizeLimitBytes = FileRequestMaxBytes + 1024 * 1024;
    }

    /// <summary>The webhook trigger's intake settings, read from the saved node parameters.</summary>
    public sealed class WebhookIntakeSettings
    {
        public string RequestFormat { get; init; } = WebhookRequestFormats.JsonLegacy;

        /// <summary>
        /// Whether file parts and binary bodies may be stored: only for an authenticated caller (SPEC10 D1). Not a
        /// node parameter; the intake sets it from the caller.
        /// </summary>
        public bool FilesAllowed { get; init; } = true;

        public bool IsStandard => RequestFormat == WebhookRequestFormats.Standard;

        /// <summary>The same settings with <see cref="FilesAllowed"/> set.</summary>
        public WebhookIntakeSettings WithFilesAllowed(bool allowed) => new()
        {
            RequestFormat = RequestFormat,
            FilesAllowed = allowed,
        };

        /// <summary>
        /// Only <c>requestFormat</c> = <c>standard</c> selects the Standard format; an absent, blank or unknown value
        /// is legacy JSON, so webhooks saved without it keep their behaviour. The retired <c>bodyMode</c>,
        /// <c>acceptedBodyTypes</c>, <c>filesFolderId</c>, <c>storageConfigurationName</c> and <c>allowedHeaders</c>
        /// parameters are ignored.
        /// </summary>
        public static WebhookIntakeSettings From(BsonDocument? parameters)
        {
            var format = parameters is not null && parameters.TryGetValue("requestFormat", out var value) && value.IsString
                ? value.AsString
                : string.Empty;
            return new WebhookIntakeSettings
            {
                RequestFormat = string.Equals(format, WebhookRequestFormats.Standard, StringComparison.Ordinal)
                    ? WebhookRequestFormats.Standard
                    : WebhookRequestFormats.JsonLegacy,
            };
        }
    }

    /// <summary>
    /// Which request headers a Standard item carries: all of them except credentials, proxy/infrastructure
    /// and connection headers (SPEC10 D3, K3, K4). Names are compared case-insensitively.
    /// </summary>
    public static class WebhookHeaderFilter
    {
        private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Credentials.
            "authorization", "proxy-authorization", "cookie", "set-cookie", "x-blocks-key",
            // Proxy and infrastructure.
            "forwarded", "via", "x-real-ip", "x-client-ip", "true-client-ip", "cf-connecting-ip",
            // Connection.
            "connection", "keep-alive", "proxy-connection", "te", "trailer", "transfer-encoding", "upgrade", "expect",
        };

        private static readonly string[] ExcludedPrefixes = ["x-forwarded-", "x-original-", "x-envoy-"];

        /// <summary>Credential-like custom headers, for example x-api-key or x-auth-token.</summary>
        private static readonly string[] ExcludedFragments = ["api-key", "apikey", "token", "secret", "password"];

        public static bool IsExcluded(string name) =>
            ExcludedNames.Contains(name)
            || ExcludedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || ExcludedFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A webhook request the intake refuses, with the HTTP status the controller answers and a short error kind
    /// for logs. <see cref="Exception.Message"/> is the caller-facing message and never holds request data
    /// other than a sanitized file name.
    /// </summary>
    public sealed class WebhookIntakeException : Exception
    {
        public int StatusCode { get; }
        public string ErrorKind { get; }

        public WebhookIntakeException(int statusCode, string errorKind, string message, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ErrorKind = errorKind;
        }

        public static WebhookIntakeException TooLarge(string message) => new(413, "too_large", message);
        public static WebhookIntakeException Unsupported(string message) => new(415, "unsupported_type", message);
        public static WebhookIntakeException BadRequest(string message) => new(400, InvalidBodyKind, message);

        /// <summary>A JSON body that cannot be parsed, decoded or stored; the cause is kept as the inner exception.</summary>
        public static WebhookIntakeException InvalidJson(Exception inner) => new(400, InvalidBodyKind, "Body is not valid JSON", inner);

        private const string InvalidBodyKind = "invalid_body";

        /// <summary>A file part or binary body sent to a webhook without an authenticated caller (SPEC10 K1, K2).</summary>
        public static WebhookIntakeException FilesNotAllowed() =>
            new(403, "files_not_allowed", "File uploads require an authenticated webhook");
    }
}
