using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Shared
{
    [BsonIgnoreExtraElements]
    public class WebPushSubscription
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public WebPushSubscriptionKeys Keys { get; set; } = new();
        public long? ExpirationTime { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    }

    public class WebPushSubscriptionKeys
    {
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }
}
