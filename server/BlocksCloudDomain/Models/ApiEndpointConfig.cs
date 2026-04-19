using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace BlocksCloudDomain.Models
{
    [BsonIgnoreExtraElements]
    public class ApiEndpointConfig : BaseEntity
    {
        public string Service { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public string? Description { get; set; }
    }
}
