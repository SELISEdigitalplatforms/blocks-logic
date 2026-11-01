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
        /// <summary>Never copied into an item, even when an author lists them.</summary>
        public static readonly IReadOnlySet<string> SensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization", "proxy-authorization", "cookie", "set-cookie", "x-blocks-key",
        };

        public string BodyMode { get; init; } = WebhookBodyModes.Json;
        public string FilesFolderId { get; init; } = "";
        public string StorageConfigurationName { get; init; } = "";
        public IReadOnlyList<string> AllowedHeaders { get; init; } = [];

        public bool IsAnyContent => BodyMode == WebhookBodyModes.AnyContent;

        /// <summary>An absent, blank or unknown <c>bodyMode</c> is legacy JSON (C18).</summary>
        public static WebhookIntakeSettings From(BsonDocument? parameters)
        {
            parameters ??= new BsonDocument();
            var mode = ReadString(parameters, "bodyMode");
            return new WebhookIntakeSettings
            {
                BodyMode = string.Equals(mode, WebhookBodyModes.AnyContent, StringComparison.Ordinal)
                    ? WebhookBodyModes.AnyContent
                    : WebhookBodyModes.Json,
                FilesFolderId = ReadString(parameters, "filesFolderId").Trim(),
                StorageConfigurationName = ReadString(parameters, "storageConfigurationName").Trim(),
                AllowedHeaders = ReadHeaderNames(parameters),
            };
        }

        private static string ReadString(BsonDocument parameters, string key) =>
            parameters.TryGetValue(key, out var value) && value.IsString ? value.AsString : string.Empty;

        private static IReadOnlyList<string> ReadHeaderNames(BsonDocument parameters)
        {
            if (!parameters.TryGetValue("allowedHeaders", out var value)) return [];

            // The editor saves a list; tolerate a comma-separated string from workflows built through the API.
            IEnumerable<string> names = value switch
            {
                BsonArray array => array.Where(v => v.IsString).Select(v => v.AsString),
                BsonString text => text.Value.Split(','),
                _ => [],
            };
            return names
                .Select(n => n.Trim().ToLowerInvariant())
                .Where(n => n.Length > 0 && !SensitiveHeaders.Contains(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
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
    }
}
