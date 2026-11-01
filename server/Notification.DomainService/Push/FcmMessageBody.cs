using System.Globalization;
using System.Text.Json.Nodes;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// The FCM v1 request body of one message: token, notification, data, plus the block of the device's platform
    /// (<c>android</c>, <c>apns</c> or <c>webpush</c>) built from the notification's options. Only set values are written.
    /// </summary>
    public static class FcmMessageBody
    {
        private const string HighPriority = "high";
        private const string NotificationField = "notification";
        private const string HeadersField = "headers";

        public static JsonObject Build(FcmMessage message, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(message);
            var data = new JsonObject();
            foreach (var pair in message.Data)
                data[pair.Key] = pair.Value;

            var body = new JsonObject
            {
                ["token"] = message.Token,
                [NotificationField] = new JsonObject { ["title"] = message.Title, ["body"] = message.Body },
                ["data"] = data,
            };

            var blocks = message.Blocks;
            (string Name, JsonObject? Block) platform = message.Platform switch
            {
                PushPlatforms.Android => ("android", Android(blocks?.Android)),
                PushPlatforms.Ios => ("apns", Apns(blocks?.Ios, now)),
                PushPlatforms.Web => ("webpush", WebPush(blocks?.Web, blocks?.WebLink)),
                _ => (string.Empty, null),
            };
            if (platform.Block is { Count: > 0 } block)
                body[platform.Name] = block;

            return new JsonObject { ["message"] = body };
        }

        private static JsonObject? Android(AndroidPushOptions? options)
        {
            if (options is null)
                return null;

            var android = new JsonObject();
            Put(android, "priority", options.Priority?.ToUpperInvariant());
            Put(android, "ttl", options.TtlSeconds is { } ttl ? ttl.ToString(CultureInfo.InvariantCulture) + "s" : null);
            Put(android, "collapse_key", options.CollapseKey);

            var notification = new JsonObject();
            Put(notification, "channel_id", options.ChannelId);
            Put(notification, "sound", options.Sound);
            Put(notification, "color", options.Color);
            Put(android, NotificationField, notification);
            return android;
        }

        private static JsonObject? Apns(IosPushOptions? options, DateTimeOffset now)
        {
            if (options is null)
                return null;

            var headers = new JsonObject();
            Put(headers, "apns-priority", options.Priority is null ? null : Priority(options.Priority));
            Put(headers, "apns-expiration", options.TtlSeconds is { } ttl ? Expiration(ttl, now) : null);

            var aps = new JsonObject();
            Put(aps, "sound", options.Sound);
            if (options.Badge is { } badge)
                aps["badge"] = badge;
            Put(aps, "interruption-level", options.InterruptionLevel);
            Put(aps, "thread-id", options.ThreadId);

            var apns = new JsonObject();
            Put(apns, HeadersField, headers);
            if (aps.Count > 0)
                apns["payload"] = new JsonObject { ["aps"] = aps };
            return apns;
        }

        private static JsonObject? WebPush(WebPushOptions? options, string? link)
        {
            var webpush = new JsonObject();
            if (options is not null)
            {
                var headers = new JsonObject();
                Put(headers, "Urgency", options.Urgency);
                Put(headers, "TTL", options.TtlSeconds?.ToString(CultureInfo.InvariantCulture));
                Put(webpush, HeadersField, headers);

                var notification = new JsonObject();
                Put(notification, "icon", options.Icon);
                if (options.RequireInteraction is { } requireInteraction)
                    notification["requireInteraction"] = requireInteraction;
                Put(webpush, NotificationField, notification);
            }

            if (link is not null)
                webpush["fcm_options"] = new JsonObject { ["link"] = link };
            return webpush;
        }

        /// <summary>APNs: 10 = send now, 5 = may be delayed to save power.</summary>
        private static string Priority(string priority) => priority == HighPriority ? "10" : "5";

        /// <summary>APNs expiry is a UNIX time; 0 = try once, do not store.</summary>
        private static string Expiration(int ttlSeconds, DateTimeOffset now) =>
            ttlSeconds == 0 ? "0" : now.AddSeconds(ttlSeconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        private static void Put(JsonObject target, string name, string? value)
        {
            if (value is not null)
                target[name] = value;
        }

        private static void Put(JsonObject target, string name, JsonObject child)
        {
            if (child.Count > 0)
                target[name] = child;
        }
    }
}
