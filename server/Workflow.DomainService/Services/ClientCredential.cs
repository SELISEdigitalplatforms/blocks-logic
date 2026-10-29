using MongoDB.Bson.Serialization.Attributes;
using DomainService.Shared;
using Blocks.Genesis;

namespace Workflow.DomainService.Services
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

        /// <summary>
        /// The Blocks Secrets id of the client secret, written by blocks-iam. When set, the value lives
        /// in the tenant's secret store and <see cref="ClientSecret"/> is empty; when null (credentials
        /// saved before), <see cref="ClientSecret"/> holds the plaintext.
        /// </summary>
        public string? ClientSecretReference { get; set; }

        public List<string> Roles { get; set; }
        public bool IsActive { get; set; }
        public List<string> Audiences { get; set; }
    }
}
