using Workflow.DomainService.Services;

namespace Workflow.DomainService.Repositories
{
    public interface IClientCredentialRepository
    {
        /// <summary>
        /// The IAM client credential with this id in the tenant's database, secret included; null when there is none.
        /// </summary>
        Task<ClientCredential?> GetClientCredentialAsync(string tenantId, string clientId);
    }
}
