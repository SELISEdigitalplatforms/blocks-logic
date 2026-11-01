using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// One device token for mobile push, in <c>NotificationDevices</c> (tenant DB; ROOT DB for root console users).
    /// The id is the SHA-256 of the token, so a token is stored once and long tokens need no big index.
    /// Rows not seen for <see cref="MaxIdle"/> are removed by a TTL index on <see cref="LastSeenAt"/> and never sent to.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class NotificationDevice
    {
        public const string CollectionName = "NotificationDevices";
        public static readonly TimeSpan MaxIdle = TimeSpan.FromDays(60);

        [BsonId]
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;

        /// <summary>Device model the app sent (for example "Pixel 8"); display only.</summary>
        [BsonIgnoreIfNull]
        public string? Name { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastSeenAt { get; set; }

        public static string IdOf(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        /// <summary>The oldest <see cref="LastSeenAt"/> still sent to at <paramref name="now"/>.</summary>
        public static DateTime SeenSince(DateTime now) => now - MaxIdle;

        /// <summary>True for a 64-character lower-case hex id as <see cref="IdOf"/> makes.</summary>
        public static bool IsId(string? id) =>
            id is { Length: 64 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }

    /// <summary>Which DB a device read or write goes to: the tenant DB, or the ROOT DB for root console users.</summary>
    public sealed record PushDb(string TenantId, bool Root);
}
