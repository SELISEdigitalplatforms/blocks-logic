using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// One device token for mobile push, in <c>NotificationDevices</c> (tenant DB; ROOT DB for root console users).
    /// The id is the SHA-256 of the token, so a token is stored once and long tokens need no big index.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class NotificationDevice
    {
        public const string CollectionName = "NotificationDevices";

        [BsonId]
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastSeenAt { get; set; }

        public static string IdOf(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    /// <summary>Which DB a device read or write goes to: the tenant DB, or the ROOT DB for root console users.</summary>
    public sealed record PushDb(string TenantId, bool Root);
}
