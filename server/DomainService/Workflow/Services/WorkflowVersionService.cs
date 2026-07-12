using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Services
{
    public class WorkflowVersionService : IWorkflowVersionService
    {
        private readonly IWorkflowVersionRepository _workflowVersionRepository;
        private readonly IWorkflowRepository _workflowRepository;
        private readonly ILogger<WorkflowVersionService> _logger;

        public WorkflowVersionService(
            IWorkflowVersionRepository workflowVersionRepository,
            IWorkflowRepository workflowRepository,
            ILogger<WorkflowVersionService> logger
        )
        {
            _workflowVersionRepository = workflowVersionRepository;
            _workflowRepository = workflowRepository;
            _logger = logger;
        }

        public async Task<BaseMutationResponse> CreateVersionAsync(string tenantId, WorkflowVersionCreateRequestDto dto)
        {
            _logger.LogInformation("Creating workflow version for ProjectKey: {ProjectKey}, WorkflowId: {WorkflowId}", tenantId, dto.WorkflowId);
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);

            if (workflow == null)
            {
                _logger.LogWarning("Workflow with Id {WorkflowId} not found for creating version.", dto.WorkflowId);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } }
                };
            }
            var version = new WorkflowVersionEntity
            {
                ItemId = Guid.NewGuid().ToString().Replace("-", ""),
                WorkflowId = workflow.ItemId,
                TenantId = workflow.TenantId,
                Name = dto.Name,
                Description = dto.Description,
                Snapshot = workflow,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext().UserId ?? "system",
                LastUpdatedBy = BlocksContext.GetContext().UserId ?? "system",
            };
            try
            {
                await _workflowVersionRepository.CreateWorkflowVersionAsync(version);
                _logger.LogInformation("Successfully created workflow version with Id: {SnapshotId} for WorkflowId: {WorkflowId}", version.ItemId, dto.WorkflowId);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = version.ItemId,
                    Errors = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating workflow version for WorkflowId: {WorkflowId}: {Message}", dto.WorkflowId, ex.Message);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Failed to create workflow version" } }
                };
            }
        }

        public async Task<BaseMutationResponse> UpdateVersionAsync(string tenantId, WorkflowVersionUpdateRequestDto dto)
        {
            _logger.LogInformation($"Updating workflow version for tenantId: {tenantId}, VersionId: {dto.VersionId}");
            var version = await _workflowVersionRepository.GetWorkflowVersionAsync(tenantId, dto.VersionId);

            if (version == null)
            {
                _logger.LogWarning($"Workflow version with Id {dto.VersionId} not found for update.");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow version not found" } }
                };
            }

            version.Name = dto.Name ?? version.Name;
            version.Description = dto.Description ?? version.Description;
            version.LastUpdatedDate = DateTime.UtcNow;
            version.LastUpdatedBy = BlocksContext.GetContext().UserId ?? "system";

            try
            {
                await _workflowVersionRepository.UpdateWorkflowVersionAsync(tenantId, dto.VersionId, version);
                _logger.LogInformation("Successfully updated workflow version with Id: {VersionId}", dto.VersionId);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = version.ItemId,
                    Errors = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating workflow version for tenantId = {tenantId} , VersionId: {dto.VersionId}.");
                _logger.LogError($"Error message {ex.StackTrace}");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Failed to update workflow version" } }
                };
            }
        }

        public async Task<WorkflowGetVersionsResponseDto> GetWorkflowVersionsAsync(string tenantId, WorkflowGetVersionsRequestDto dto)
        {
            try
            {
                var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
                if (workflow == null)
                {
                    _logger.LogWarning("Workflow with Id {WorkflowId} not found for fetching versions.", dto.WorkflowId);
                    return new WorkflowGetVersionsResponseDto
                    {
                        Data = null,
                        TotalCount = 0,
                        Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } }
                    };
                }
                _logger.LogInformation($"Fetching workflow versions for tenantId: {tenantId}, WorkflowId: {dto.WorkflowId}");
                var versions = await _workflowVersionRepository.GetWorkflowVersionsAsync(tenantId, dto.WorkflowId);
                _logger.LogInformation("Successfully fetched {Count} workflow versions for ProjectKey: {ProjectKey}, WorkflowId: {WorkflowId}", versions.Count, tenantId, dto.WorkflowId);

                var versionSummaries = versions.Select(v => new WorkflowGetVersionSummary
                {
                    ItemId = v.ItemId,
                    WorkflowId = v.WorkflowId,
                    TenantId = v.TenantId,
                    Name = v.Name,
                    Description = v.Description,
                    IsPublished = workflow.PublishedVersionId == v.ItemId,
                    CreatedDate = v.CreatedDate,
                    LastUpdatedDate = v.LastUpdatedDate,
                    CreatedBy = v.CreatedBy,
                    LastUpdatedBy = v.LastUpdatedBy
                }).ToList();

                return new WorkflowGetVersionsResponseDto
                {
                    Data = versionSummaries,
                    TotalCount = versionSummaries.Count,
                    Errors = null,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching workflow versions for ProjectKey: {tenantId}, WorkflowId: {WorkflowId}: {Message}", tenantId, dto.WorkflowId, ex.Message);
                return new WorkflowGetVersionsResponseDto
                {
                    Data = null,
                    TotalCount = 0,
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } },
                };
            }
        }
    }
}