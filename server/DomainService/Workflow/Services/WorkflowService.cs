using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Models;
using DomainService.Workflow.Repositories;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Newtonsoft.Json;

namespace DomainService.Workflow.Services
{
    [ExcludeFromCodeCoverage]
    public class WorkflowService : IWorkflowService
    {
        private readonly IWorkflowRepository _repository;
        private readonly ILogger<WorkflowService> _logger;



        public WorkflowService(IWorkflowRepository repository, ILogger<WorkflowService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Creates an error response for project key not found exception
        /// </summary>
        private BaseMutationResponse CreateProjectKeyNotFoundError(string projectKey, string context, string stackTrace)
        {
            _logger.LogError("Error {Context}: {StackTrace}", context, stackTrace);
            return new BaseMutationResponse
            {
                IsSuccess = false,
                ItemId = null,
                Errors = new Dictionary<string, string> { { "Message", $"Project key is not found {projectKey}" } }
            };
        }

        /// <summary>
        /// Creates an error response for general exceptions
        /// </summary>
        private BaseMutationResponse CreateGeneralError(string context, Exception ex)
        {
            _logger.LogError("Error {Context}: {StackTrace}", context, ex.StackTrace);
            return new BaseMutationResponse
            {
                IsSuccess = false,
                ItemId = null,
                Errors = new Dictionary<string, string> { { "Message", ex.Message } }
            };
        }

        /// <summary>
        /// Safely gets a workflow and handles errors using common error responses
        /// </summary>
        private async Task<(WorkflowModel? workflow, BaseMutationResponse? errorResponse)> TryGetWorkflowAsync(
            string workflowId,
            string projectKey,
            string context)
        {
            try
            {
                var workflow = await _repository.GetWorkflowAsync(workflowId, projectKey);
                return (workflow, null);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is KeyNotFoundException)
            {
                return (null, CreateProjectKeyNotFoundError(projectKey, context, ex.StackTrace ?? ""));
            }
            catch (Exception ex)
            {
                return (null, CreateGeneralError($"while {context}", ex));
            }
        }




        public async Task<BaseMutationResponse> CreateAsync(WorkflowCreateRequestDto dto)

        {
            _logger.LogInformation($"Creating workflow for ProjectKey: {dto.ProjectKey}, Name: {dto.Name},");
            var model = new WorkflowModel
            {
                ItemId = Guid.NewGuid().ToString().Replace("-", ""),
                Name = dto.Name,
                TenantId = dto.ProjectKey,
                Nodes = JsonConvert.DeserializeObject<List<NodeModel>>(dto.Nodes.GetRawText()) ?? new(),
                Edges = dto.Edges,
                Settings = dto.Settings,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext().UserId ?? "system",
                LastUpdatedBy = BlocksContext.GetContext().UserId ?? "system",
                Description = dto.Description,
                NodeOutputSchemas = dto.NodeOutputSchemas
            };
            _logger.LogInformation("Inserting workflow into repository: {Model}", JsonConvert.SerializeObject(model));
            try
            {
                await _repository.CreateWorkflowAsync(model);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is KeyNotFoundException)
            {
                return CreateProjectKeyNotFoundError(dto.ProjectKey, "inserting workflow", ex.StackTrace ?? "");
            }
            catch (Exception ex)
            {
                return CreateGeneralError("inserting workflow", ex);
            }
            _logger.LogInformation("Successfully created workflow with Id: {WorkflowId}", model.ItemId);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = model.ItemId,
                Errors = null
            };
        }

        public async Task<BaseMutationResponse> DuplicateAsync(WorkflowDuplicateRequestDto dto)
        {
            _logger.LogInformation("Duplicating workflow with Dto: {Dto}", JsonConvert.SerializeObject(dto));

            var (existingWorkflow, errorResponse) = await TryGetWorkflowAsync(dto.WorkflowId, dto.ProjectKey, "duplicating workflow");
            if (errorResponse != null)
                return errorResponse;

            if (existingWorkflow == null)
            {
                _logger.LogError("Workflow with Id {WorkflowId} not found for duplication.", dto.WorkflowId);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }

            existingWorkflow.ItemId = Guid.NewGuid().ToString().Replace("-", "");
            existingWorkflow.Name = dto.Name;
            existingWorkflow.CreatedDate = DateTime.UtcNow;
            existingWorkflow.LastUpdatedDate = DateTime.UtcNow;
            existingWorkflow.CreatedBy = BlocksContext.GetContext().UserId ?? "";
            existingWorkflow.LastUpdatedBy = BlocksContext.GetContext().UserId ?? "";
            existingWorkflow.IsActive = true;

            try
            {
                _logger.LogInformation("Start duplicating workflow with Id: {WorkflowId}", dto.WorkflowId);
                await _repository.CreateWorkflowAsync(existingWorkflow);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error duplicating workflow: {StackTrace}", ex.StackTrace);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", ex.Message } }
                };
            }
            _logger.LogInformation("Successfully duplicated workflow with new Id: {WorkflowId}", existingWorkflow.ItemId);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = existingWorkflow.ItemId,
                Errors = null
            };
        }
        public async Task<WorkflowGetsResponseDto> GetAllAsync(WorkflowGetsRequestDto dto)
        {
            _logger.LogInformation($"Fetching workflows. ProjectKey: {dto.ProjectKey}, Page: {dto.PageNumber}, PageSize: {dto.PageSize}, Search: {dto.Search}, IsActive: {dto.IsActive}");

            var workflows = await _repository.GetAllWorkflowsAsync(dto.PageSize, dto.PageNumber, dto.Search, dto.IsActive, dto.ProjectKey);
            var totalCount = await _repository.GetWorkflowsCountAsync(dto.Search, dto.IsActive, dto.ProjectKey);

            var workflowDtos = workflows.Select(w => new WorkflowItemDto
            {
                ItemId = w.ItemId,
                Name = w.Name,
                Settings = w.Settings ?? new Dictionary<string, string>(),
                CreatedDate = w.CreatedDate,
                LastUpdatedDate = w.LastUpdatedDate,
                CreatedBy = w.CreatedBy,
                LastUpdatedBy = w.LastUpdatedBy,
                Language = w.Language,
                Tags = w.Tags ?? new List<string>(),
                IsActive = w.IsActive,
            }).ToList();

            _logger.LogInformation("Completed fetching workflows. ProjectKey: {ProjectKey}, Page: {Page}, PageSize: {PageSize}, Search: {Search}, IsActive: {IsActive}", dto.ProjectKey, dto.PageNumber, dto.PageSize, dto.Search, dto.IsActive);

            return new WorkflowGetsResponseDto
            {
                Data = workflowDtos,
                TotalCount = totalCount
            };
        }

        public async Task<WorkflowGetResponseDto> GetAsync(WorkflowGetRequestDto dto)
        {
            _logger.LogInformation($"Fetching workflow for ProjectKey: {dto.ProjectKey}, WorkflowId: {dto.WorkflowId}");
            WorkflowModel workflow;
            try
            {
                workflow = await _repository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is KeyNotFoundException)
            {
                _logger.LogError("Error fetching workflow: {StackTrace}", ex.StackTrace);
                return new WorkflowGetResponseDto
                {
                    IsSuccess = false,
                    data = null,
                    Errors = new Dictionary<string, string> { { "Message", $"Project key is not found {dto.ProjectKey}" } }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while fetching workflow: {StackTrace}", ex.StackTrace);
                return new WorkflowGetResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", ex.Message } },
                    data = null
                };
            }

            if (workflow == null)
            {
                _logger.LogError("Workflow with Id {WorkflowId} not found.", dto.WorkflowId);
                return new WorkflowGetResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    data = null
                };
            }
            _logger.LogInformation("Successfully fetched workflow with Id: {WorkflowId}", dto.WorkflowId);
            var nodes = workflow.Nodes.Select(n => new NodeDto
            {
                Id = n.Id,
                Name = n.Name,
                Category = n.Category,
                Type = n.Type,
                Version = n.Version,
                Position = n.Position,
                Parameters = JsonDocument.Parse(n.Parameters.ToJson()).RootElement,
                Settings = JsonDocument.Parse(n.Settings.ToJson()).RootElement
            }).ToList();
            var workflowDto = new Dtos.Workflow
            {
                ItemId = workflow.ItemId,
                Name = workflow.Name,
                TenantId = workflow.TenantId,
                Nodes = nodes,
                Edges = workflow.Edges,
                Settings = workflow.Settings ?? new Dictionary<string, string>(),
                IsActive = workflow.IsActive,
                Description = workflow.Description,
                NodeOutputSchemas = workflow.NodeOutputSchemas,
                LastUpdatedDate = workflow.LastUpdatedDate,
                CreatedDate = workflow.CreatedDate,
                CreatedBy = workflow.CreatedBy,
                LastUpdatedBy = workflow.LastUpdatedBy,
            };
            return new WorkflowGetResponseDto
            {
                IsSuccess = true,
                Errors = null,
                data = workflowDto
            };

        }


        public async Task<BaseMutationResponse> UpdateAsync(WorkflowUpdateRequestDto dto)
        {
            _logger.LogInformation("Updating workflow with Dto: {Dto}", JsonConvert.SerializeObject(dto));

            var (workflow, errorResponse) = await TryGetWorkflowAsync(dto.ItemId, dto.ProjectKey, "updating workflow");
            if (errorResponse != null)
                return errorResponse;

            if (workflow == null)
            {
                _logger.LogError("Workflow with Id {WorkflowId} not found for update.", dto.ItemId);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } }
                };
            }

            // Replace simple properties
            workflow.Name = dto.Name ?? workflow.Name;
            workflow.IsActive = dto.IsActive ?? workflow.IsActive;
            workflow.Edges = dto.Edges ?? workflow.Edges;
            workflow.NodeOutputSchemas = dto.NodeOutputSchemas ?? workflow.NodeOutputSchemas;
            if (dto.Nodes != null)
            {
                workflow.Nodes = dto.Nodes.Select(n => new NodeModel
                {
                    Name = n.Name,
                    Id = n.Id,
                    Category = n.Category,
                    Type = n.Type,
                    Version = n.Version,
                    Position = n.Position,
                    Parameters = BsonDocument.Parse(n.Parameters.GetRawText()),
                    Settings = BsonDocument.Parse(n.Settings.GetRawText())
                }).ToList();
            }
            workflow.LastUpdatedDate = DateTime.UtcNow;
            workflow.LastUpdatedBy = BlocksContext.GetContext().UserId ?? "system";

            await _repository.UpdateWorkflowAsync(workflow);

            _logger.LogInformation("Successfully updated workflow with Id: {WorkflowId}", workflow.ItemId);

            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = workflow.ItemId,
                Errors = null
            };
        }



        public async Task<BaseMutationResponse> DeleteAsync(WorkflowDeleteRequestDto dto)
        {
            _logger.LogInformation("Deleting workflow with Dto: {Dto}", JsonConvert.SerializeObject(dto));

            var (existingWorkflow, errorResponse) = await TryGetWorkflowAsync(dto.Id, dto.ProjectKey, "deleting workflow");
            if (errorResponse != null)
                return errorResponse;

            if (existingWorkflow == null)
            {
                _logger.LogWarning("Workflow with Id {WorkflowId} not found for deletion.", dto.Id);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }
            try
            {
                _logger.LogInformation("Start deleting workflow with Id: {WorkflowId}", dto.Id);
                await _repository.DeleteWorkflowAsync(dto.Id, dto.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error deleting workflow: {StackTrace}", ex.StackTrace);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", ex.Message } }
                };
            }
            _logger.LogInformation("Successfully deleted workflow with Id: {WorkflowId}", dto.Id);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = dto.Id
            };
        }
    }

}
