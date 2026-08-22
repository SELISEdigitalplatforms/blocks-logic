using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared;

namespace DomainService.Projects
{
    public interface IProjectRepository
    {
        Task<List<GroupedProjectsDto>> GetAllByLastModifiedDateAsync(GetProjectsRequest request);
        Task<Tenant> GetByTenantIdAsync(string tenantId);
        Task<BlocksGuid> GetBlocksGuidAsync(string tenantGroupId);
        Task<List<Project>> GetProjectPeoplesAsync(string tenantGroupId);
        Task<List<string>> GetProjectIdsByGroupId(string projectGroupId);
    }
}
