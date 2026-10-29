namespace DomainService.Notification.Push
{
    /// <summary>
    /// Limits of push options (contract §2): checks a draft's options, and keeps only valid values of stored options
    /// (a bad stored value is left out, never sent). Error texts never repeat the value.
    /// </summary>
    public static class PushOptionRules
    {
        public const int MaxTtlSeconds = 2_419_200;
        public const int MaxBadge = 9999;
        public const int MaxTextLength = 100;
        public const int MaxLinkLength = 500;

        private const string TokenChars = "._{}-";
        private const string HttpsPrefix = "https://";
        private static readonly string[] Priorities = ["high", "normal"];
        private static readonly string[] InterruptionLevels = ["active", "time-sensitive", "passive"];
        private static readonly string[] Urgencies = ["very-low", "low", "normal", "high"];

        /// <summary>Field → message for every value out of its limits. Empty when all is valid.</summary>
        public static Dictionary<string, string> Check(PushNotificationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            Expect(errors, "webLink", options.WebLink, IsHttpsLink(options.WebLink), "Web link must be an https link of at most 500 characters.");
            if (options.Platforms?.Any(p => PushPlatforms.Normalize(p) is null) == true)
                errors["platforms"] = "Platforms must be android, ios or web.";
            if (options.Android is { } android)
                CheckAndroid(errors, android);
            if (options.Ios is { } ios)
                CheckIos(errors, ios);
            if (options.Web is { } web)
                CheckWeb(errors, web);
            return errors;
        }

        /// <summary>Only the valid values, with placeholders of CollapseKey, ThreadId and WebLink filled. Null for null.</summary>
        public static PushPlatformBlocks? Resolve(PushNotificationOptions? options, IReadOnlyDictionary<string, string> fields)
        {
            if (options is null)
                return null;

            var android = options.Android is { } a ? ResolveAndroid(a, fields) : null;
            var ios = options.Ios is { } i ? ResolveIos(i, fields) : null;
            var web = options.Web is { } w ? ResolveWeb(w) : null;
            var webLink = IsHttpsLink(options.WebLink) && options.WebLink is { } link
                ? PushTextRenderer.Fill(link, fields, MaxLinkLength, escapeValues: true, singleBraces: true)
                : null;
            return new PushPlatformBlocks(android, ios, web, webLink);
        }

        private static AndroidPushOptions ResolveAndroid(AndroidPushOptions a, IReadOnlyDictionary<string, string> fields) => new()
        {
            Priority = OneOf(a.Priority, Priorities),
            ChannelId = Token(a.ChannelId),
            Sound = Token(a.Sound),
            TtlSeconds = Ttl(a.TtlSeconds),
            CollapseKey = Filled(Token(a.CollapseKey), fields),
            Color = IsColor(a.Color) ? a.Color : null,
        };

        private static IosPushOptions ResolveIos(IosPushOptions i, IReadOnlyDictionary<string, string> fields) => new()
        {
            Sound = Token(i.Sound),
            Badge = i.Badge is >= 0 and <= MaxBadge ? i.Badge : null,
            Priority = OneOf(i.Priority, Priorities),
            InterruptionLevel = OneOf(i.InterruptionLevel, InterruptionLevels),
            ThreadId = Filled(Token(i.ThreadId), fields),
            TtlSeconds = Ttl(i.TtlSeconds),
        };

        private static WebPushOptions ResolveWeb(WebPushOptions w) => new()
        {
            Icon = IsHttpsLink(w.Icon) ? w.Icon : null,
            RequireInteraction = w.RequireInteraction,
            Urgency = OneOf(w.Urgency, Urgencies),
            TtlSeconds = Ttl(w.TtlSeconds),
        };

        /// <summary>An https link of at most 500 characters without spaces.</summary>
        public static bool IsHttpsLink(string? value) =>
            value is { Length: > 8 and <= MaxLinkLength } && value.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase)
            && !value.Any(char.IsWhiteSpace);

        private static void CheckAndroid(Dictionary<string, string> errors, AndroidPushOptions o)
        {
            Expect(errors, "android.priority", o.Priority, OneOf(o.Priority, Priorities) is not null, "Priority must be high or normal.");
            ExpectToken(errors, "android.channelId", o.ChannelId);
            ExpectToken(errors, "android.sound", o.Sound);
            ExpectTtl(errors, "android.ttlSeconds", o.TtlSeconds);
            ExpectToken(errors, "android.collapseKey", o.CollapseKey);
            Expect(errors, "android.color", o.Color, IsColor(o.Color), "Colour must look like #1A73E8.");
        }

        private static void CheckIos(Dictionary<string, string> errors, IosPushOptions o)
        {
            ExpectToken(errors, "ios.sound", o.Sound);
            if (o.Badge is < 0 or > MaxBadge)
                errors["ios.badge"] = $"Badge must be between 0 and {MaxBadge}.";
            Expect(errors, "ios.priority", o.Priority, OneOf(o.Priority, Priorities) is not null, "Priority must be high or normal.");
            Expect(errors, "ios.interruptionLevel", o.InterruptionLevel, OneOf(o.InterruptionLevel, InterruptionLevels) is not null,
                "Interruption level must be active, time-sensitive or passive.");
            ExpectToken(errors, "ios.threadId", o.ThreadId);
            ExpectTtl(errors, "ios.ttlSeconds", o.TtlSeconds);
        }

        private static void CheckWeb(Dictionary<string, string> errors, WebPushOptions o)
        {
            Expect(errors, "web.icon", o.Icon, IsHttpsLink(o.Icon), "Icon must be an https link of at most 500 characters.");
            Expect(errors, "web.urgency", o.Urgency, OneOf(o.Urgency, Urgencies) is not null, "Urgency must be very-low, low, normal or high.");
            ExpectTtl(errors, "web.ttlSeconds", o.TtlSeconds);
        }

        private static void Expect(Dictionary<string, string> errors, string field, string? value, bool valid, string message)
        {
            if (value is not null && !valid)
                errors[field] = message;
        }

        private static void ExpectToken(Dictionary<string, string> errors, string field, string? value) =>
            Expect(errors, field, value, Token(value) is not null, "Use at most 100 letters, digits, dots, dashes, underscores or {field}.");

        private static void ExpectTtl(Dictionary<string, string> errors, string field, int? value)
        {
            if (value is not null && Ttl(value) is null)
                errors[field] = "Expiry must be between 0 and 4 weeks.";
        }

        private static string? OneOf(string? value, string[] allowed) =>
            value is not null && allowed.Contains(value, StringComparer.Ordinal) ? value : null;

        private static string? Token(string? value) =>
            value is { Length: > 0 and <= MaxTextLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || TokenChars.Contains(c)) ? value : null;

        private static int? Ttl(int? value) => value is >= 0 and <= MaxTtlSeconds ? value : null;

        private static bool IsColor(string? value) =>
            value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);

        private static string? Filled(string? value, IReadOnlyDictionary<string, string> fields) =>
            value is null ? null : PushTextRenderer.Fill(value, fields, MaxTextLength, singleBraces: true);
    }
}
