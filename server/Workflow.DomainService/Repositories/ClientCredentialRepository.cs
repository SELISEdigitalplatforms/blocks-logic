using Blocks.Genesis;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using Workflow.DomainService.Services;

namespace Workflow.DomainService.Repositories
{
    /// <summary>
    /// Reads IAM's <c>ClientCredentials</c> collection so a workflow node can resolve the secret at run time
    /// instead of storing it in the node parameters.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ClientCredentialRepository : IClientCredentialRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "ClientCredentials";

        public ClientCredentialRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<ClientCredential?> GetClientCredentialAsync(string tenantId, string clientId)
        {
            // System read for execution: not through the data access policy, whose View rules mask ClientSecret.
            // The tenant id picks the tenant database; IAM does not stamp TenantId on these documents.
            var collection = _dbContextProvider.GetCollection<ClientCredential>(tenantId, _collectionName);
            var filter = Builders<ClientCredential>.Filter.Eq(c => c.ItemId, clientId);
            var projection = Builders<ClientCredential>.Projection
                .Include(c => c.ItemId)
                .Include(c => c.ClientSecret)
                .Include(c => c.ClientSecretReference)
                .Include(c => c.IsActive);

            return await collection.Find(filter).Project<ClientCredential>(projection).FirstOrDefaultAsync();
        }
    }
}
