using MongoDB.Bson.Serialization.Attributes;
using DomainService.Shared;
using Blocks.Genesis;

namespace DomainService.MagicLink.Models
{
    /// <summary>
    /// Represents a client credentials entity for OAuth authentication.
    /// Collection: ClientCredentials
    /// </summary>
    [BsonIgnoreExtraElements]
    public class ClientCredential : BaseEntity
    {
        public string Name { get; set; }
        public string ClientSecret { get; set; }
        public List<string> Roles { get; set; }
        public bool IsActive { get; set; }
        public List<string> Audiences { get; set; }
    }
}
