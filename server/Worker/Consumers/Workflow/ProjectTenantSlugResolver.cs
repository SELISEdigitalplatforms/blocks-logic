using Blocks.Genesis;
using DomainService.Projects;
using DomainService.Shared;
using Workflow.DomainService.Import;

namespace Worker.Consumers.Workflow
{
    public sealed class ProjectTenantSlugResolver : IWorkflowImportTenantSlugResolver
    {
        private readonly IProjectRepository _projectRepository;

        public ProjectTenantSlugResolver(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;
        }

        public async Task<string?> ResolveAsync(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return null;
            }

            var project = await _projectRepository.GetByTenantIdAsync(tenantId);
            if (project is null || string.IsNullOrWhiteSpace(project.TenantGroupId))
            {
                return null;
            }

            var blocksGuid = await _projectRepository.GetBlocksGuidAsync(project.TenantGroupId);
            if (blocksGuid is null || string.IsNullOrWhiteSpace(blocksGuid.EncodedValue))
            {
                return null;
            }

            return $"{IdentifierHelper.EnvironmentMapper(project.Environment)}{blocksGuid.EncodedValue}";
        }
    }
}
