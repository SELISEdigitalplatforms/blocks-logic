using MongoDB.Bson;

namespace Workflow.DomainService.Services
{
    /// <summary>Webhook body modes, as saved in the trigger's <c>bodyMode</c> parameter.</summary>
    public static class WebhookBodyModes
    {
        /// <summary>Legacy: a JSON object or an array of objects; items are the JSON itself.</summary>
        public const string Json = "json";

        /// <summary>JSON, forms, multipart, text and binary, each turned into the item wrapper.</summary>
        public const string AnyContent = "anyContent";
    }

    /// <summary>Request limits for the Any content body mode (SPEC8 § Limits).</summary>
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
        /// <summary>The body types an author can restrict an Any content webhook to (SPEC10 D6).</summary>
        public static readonly IReadOnlySet<string> BodyTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            WebhookContentReader.FormatJson, WebhookContentReader.FormatForm, WebhookContentReader.FormatMultipart,
            WebhookContentReader.FormatText, WebhookContentReader.FormatBinary,
        };

        public string BodyMode { get; init; } = WebhookBodyModes.Json;

        /// <summary>Empty accepts every type. Only read in Any content mode (SPEC10 K11).</summary>
        public IReadOnlySet<string> AcceptedBodyTypes { get; init; } = new HashSet<string>();

        /// <summary>
        /// Whether file parts and binary bodies may be stored: only for an authenticated caller (SPEC10 D1). Not a
        /// node parameter; the intake sets it from the caller.
        /// </summary>
        public bool FilesAllowed { get; init; } = true;

        public bool IsAnyContent => BodyMode == WebhookBodyModes.AnyContent;

        /// <summary>True when <paramref name="bodyFormat"/> passes the author's restriction; an empty body always does.</summary>
        public bool Accepts(string bodyFormat) =>
            AcceptedBodyTypes.Count == 0
            || bodyFormat == WebhookContentReader.FormatEmpty
            || AcceptedBodyTypes.Contains(bodyFormat);

        /// <summary>The same settings with <see cref="FilesAllowed"/> set.</summary>
        public WebhookIntakeSettings WithFilesAllowed(bool allowed) => new()
        {
            BodyMode = BodyMode,
            AcceptedBodyTypes = AcceptedBodyTypes,
            FilesAllowed = allowed,
        };

        /// <summary>
        /// An absent, blank or unknown <c>bodyMode</c> is legacy JSON (C18). The retired <c>filesFolderId</c>,
        /// <c>storageConfigurationName</c> and <c>allowedHeaders</c> parameters are ignored (SPEC10 N3).
        /// </summary>
        public static WebhookIntakeSettings From(BsonDocument? parameters)
        {
            parameters ??= new BsonDocument();
            var mode = ReadString(parameters, "bodyMode");
            return new WebhookIntakeSettings
            {
                BodyMode = string.Equals(mode, WebhookBodyModes.AnyContent, StringComparison.Ordinal)
                    ? WebhookBodyModes.AnyContent
                    : WebhookBodyModes.Json,
                AcceptedBodyTypes = ReadBodyTypes(parameters),
            };
        }

        private static string ReadString(BsonDocument parameters, string key) =>
            parameters.TryGetValue(key, out var value) && value.IsString ? value.AsString : string.Empty;

        /// <summary>Known values only, trimmed and lower-cased; only unknown values means no restriction (K10).</summary>
        private static HashSet<string> ReadBodyTypes(BsonDocument parameters)
        {
            if (!parameters.TryGetValue("acceptedBodyTypes", out var value)) return new HashSet<string>();

            // The editor saves a list; tolerate a comma-separated string from workflows built through the API.
            IEnumerable<string> names = value switch
            {
                BsonArray array => array.Where(v => v.IsString).Select(v => v.AsString),
                BsonString text => text.Value.Split(','),
                _ => [],
            };
            return names
                .Select(n => n.Trim().ToLowerInvariant())
                .Where(BodyTypes.Contains)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Which request headers an Any content item carries: all of them except credentials, proxy/infrastructure
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
        public static WebhookIntakeException BadRequest(string message) => new(400, "invalid_body", message);

        /// <summary>A file part or binary body sent to a webhook without an authenticated caller (SPEC10 K1, K2).</summary>
        public static WebhookIntakeException FilesNotAllowed() =>
            new(403, "files_not_allowed", "File uploads require an authenticated webhook");

        /// <summary>A body type outside the webhook's Accepted Body Types (SPEC10 K9).</summary>
        public static WebhookIntakeException TypeNotAccepted(string bodyFormat) =>
            new(415, "unsupported_type", $"This webhook does not accept {bodyFormat} bodies");
    }
}
