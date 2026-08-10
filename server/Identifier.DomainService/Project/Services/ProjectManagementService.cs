using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.Projects
{
    public class ProjectManagementService : IProjectManagementService
    {
        private readonly IProjectRepository _projectRepository;

        public ProjectManagementService(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;
        }

        public async Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request)
        {
            return await _projectRepository.GetAllByLastModifiedDateAsync(request);
        }

        public async Task<GetProjectResponse> GetAsync()
        {
            return await MapIntoProjectAsync();
        }

        private async Task<GetProjectResponse> MapIntoProjectAsync()
        {
            var context = BlocksContext.GetContext();
            if (context is null)
            {
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "context_not_found", "BlocksContext is null" } } };
            }
            var tenantId = context.TenantId;
            var repoProject = await _projectRepository.GetByTenantIdAsync(tenantId);

            if (repoProject == null)
            {
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "project_not_exist", $"project_with_id_{tenantId}_not_exist_into_our_system" } } };
            }

            string tenantSlug = string.Empty;
            var blocksGuid = await _projectRepository.GetBlocksGuidAsync(repoProject.TenantGroupId);
            if (blocksGuid is not null)
            {
                tenantSlug = $"{IdentifierHelper.EnvironmentMapper(repoProject.Environment)}{blocksGuid.EncodedValue}";
            }

            var project = new GetProjectResponseData
            {
                Name = repoProject.Name,
                Applications = repoProject.Applications,
                ItemId = repoProject.ItemId,
                CreatedDate = repoProject.CreatedDate,
                LastUpdatedDate = repoProject.LastUpdatedDate,
                LastUpdatedBy = repoProject.LastUpdatedBy,
                CreatedBy = repoProject.CreatedBy,
                Tags = repoProject.Tags,
                TenantId = repoProject.TenantId,
                IsDomainVerified = repoProject.Applications.FirstOrDefault()?.IsDomainVerified ?? false,
                IsDisabled = repoProject.IsDisabled,
                Environment = repoProject.Environment,
                TenantGroupId = repoProject.TenantGroupId,
                TenantSlug = tenantSlug
            };

            return new GetProjectResponse { Data = project };
        }
    }
}