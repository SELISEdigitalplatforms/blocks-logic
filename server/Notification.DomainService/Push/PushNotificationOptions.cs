using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// KV <c>notification-push-options:{ConfigName}</c> (tenant DB): push platforms and per-platform options of one
    /// notification configuration. blocks-os writes it; logic reads it. Missing record = today's message to every platform.
    /// Also the shape of the options in a draft test request.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class PushNotificationOptions
    {
        public const string KeyPrefix = "notification-push-options:";

        public string ConfigName { get; set; } = string.Empty;

        /// <summary><c>android</c>, <c>ios</c>, <c>web</c>; missing or empty = all three.</summary>
        public List<string>? Platforms { get; set; }

        /// <summary>https link opened when a web push is clicked; may hold placeholders.</summary>
        public string? WebLink { get; set; }

        public AndroidPushOptions? Android { get; set; }
        public IosPushOptions? Ios { get; set; }
        public WebPushOptions? Web { get; set; }

        /// <summary>True when the notification is sent to <paramref name="platform"/>.</summary>
        public static bool Allows(IReadOnlyCollection<string>? platforms, string platform) =>
            platforms is null || platforms.Count == 0 || platforms.Any(p => PushPlatforms.Normalize(p) == platform);
    }

    [BsonIgnoreExtraElements]
    public sealed class AndroidPushOptions
    {
        /// <summary><c>high</c> or <c>normal</c>.</summary>
        public string? Priority { get; set; }
        public string? ChannelId { get; set; }
        public string? Sound { get; set; }
        public int? TtlSeconds { get; set; }
        public string? CollapseKey { get; set; }

        /// <summary><c>#RRGGBB</c>.</summary>
        public string? Color { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class IosPushOptions
    {
        public string? Sound { get; set; }
        public int? Badge { get; set; }

        /// <summary><c>high</c> or <c>normal</c>.</summary>
        public string? Priority { get; set; }

        /// <summary><c>active</c>, <c>time-sensitive</c> or <c>passive</c>.</summary>
        public string? InterruptionLevel { get; set; }
        public string? ThreadId { get; set; }
        public int? TtlSeconds { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class WebPushOptions
    {
        /// <summary>https URL of the icon.</summary>
        public string? Icon { get; set; }
        public bool? RequireInteraction { get; set; }

        /// <summary><c>very-low</c>, <c>low</c>, <c>normal</c> or <c>high</c>.</summary>
        public string? Urgency { get; set; }
        public int? TtlSeconds { get; set; }
    }

    /// <summary>
    /// Options of one push, checked and with placeholders filled, ready for the FCM body. Present = the notification
    /// has push options (then web devices get <see cref="WebLink"/> instead of the app link).
    /// </summary>
    public sealed record PushPlatformBlocks(AndroidPushOptions? Android, IosPushOptions? Ios, WebPushOptions? Web, string? WebLink);
}
