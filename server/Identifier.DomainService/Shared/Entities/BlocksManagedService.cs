using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Shared.Entities
{
    [BsonIgnoreExtraElements]
    public class BlocksManagedService : BaseEntity
    {
        public string Name { get; set; }
        public string TenantId { get; set; }
        public string Description { get; set; }
        public string ServiceId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string ServiceBusConnectionString { get; set; }

        /// <summary>
        /// The Blocks Secrets id of the connection string. When set, ServiceBusConnectionString is
        /// empty; when null, ServiceBusConnectionString holds the TenantSalt ciphertext as today.
        /// Written by blocks-os only; the element name must match.
        /// </summary>
        [BsonIgnoreIfNull]
        public string? ServiceBusConnectionStringReference { get; set; }

        public string ServiceType { get; set; }
    }

    public enum BlocksManagedServiceType
    {
        None = 0,
        Api = 1,
        Worker = 2,
    }
}
