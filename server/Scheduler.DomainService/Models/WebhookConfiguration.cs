using MongoDB.Bson.Serialization.Attributes;

namespace Scheduler.DomainService.Models
{
    [BsonIgnoreExtraElements]
    public class WebhookConfiguration
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";
        public Dictionary<string, string>? Headers { get; set; }
        public string? SigningSecret { get; set; }
    }
}
