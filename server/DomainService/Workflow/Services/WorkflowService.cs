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
        private readonly IWorkflowRepository _workflowRepository;
        private readonly IWorkflowVersionRepository _workflowVersionRepository;
        private readonly ILogger<WorkflowService> _logger;



        public WorkflowService(IWorkflowRepository workflowRepository, IWorkflowVersionRepository workflowVersionRepository, ILogger<WorkflowService> logger)
        {
            _workflowRepository = workflowRepository;
            _workflowVersionRepository = workflowVersionRepository;
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
                var workflow = await _workflowRepository.GetWorkflowAsync(workflowId, projectKey);
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
                IsDirty = true,
                IsPublished = false,
                PublishedVersionId = null,
                PublishedMeta = null,
                Settings = dto.Settings,
                Description = dto.Description,
                NodeOutputSchemas = dto.NodeOutputSchemas,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext().UserId ?? "system",
                LastUpdatedBy = BlocksContext.GetContext().UserId ?? "system",
            };
            _logger.LogInformation("Inserting workflow into repository: {Model}", JsonConvert.SerializeObject(model));
            try
            {
                await _workflowRepository.CreateWorkflowAsync(model);
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
            existingWorkflow.IsDirty = true;
            existingWorkflow.IsPublished = false;
            existingWorkflow.PublishedVersionId = null;
            existingWorkflow.PublishedMeta = null;
            existingWorkflow.CreatedDate = DateTime.UtcNow;
            existingWorkflow.LastUpdatedDate = DateTime.UtcNow;
            existingWorkflow.CreatedBy = BlocksContext.GetContext().UserId ?? "";
            existingWorkflow.LastUpdatedBy = BlocksContext.GetContext().UserId ?? "";

            try
            {
                _logger.LogInformation("Start duplicating workflow with Id: {WorkflowId}", dto.WorkflowId);
                await _workflowRepository.CreateWorkflowAsync(existingWorkflow);
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
            _logger.LogInformation($"Fetching workflows. ProjectKey: {dto.ProjectKey}, Page: {dto.PageNumber}, PageSize: {dto.PageSize}, Search: {dto.Search}, IsPublished: {dto.IsPublished}");

            var workflows = await _workflowRepository.GetAllWorkflowsAsync(dto.PageSize, dto.PageNumber, dto.Search, dto.IsPublished, dto.ProjectKey);
            var totalCount = await _workflowRepository.GetWorkflowsCountAsync(dto.Search, dto.IsPublished, dto.ProjectKey);

            var workflowDtos = workflows.Select(w => new WorkflowListItemDto
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
                IsPublished = w.IsPublished,
                IsDirty = w.IsDirty
            }).ToList();

            _logger.LogInformation("Completed fetching workflows. ProjectKey: {ProjectKey}, Page: {Page}, PageSize: {PageSize}, Search: {Search}, IsPublished: {IsPublished}", dto.ProjectKey, dto.PageNumber, dto.PageSize, dto.Search, dto.IsPublished);

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
                workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
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

            WorkflowVersionModel publishedVersion = null;
            if (workflow.IsPublished && !string.IsNullOrEmpty(workflow.PublishedVersionId))
            {
                _logger.LogInformation("Fetching published version for workflow with Id: {WorkflowId}, PublishedVersionId: {PublishedVersionId}", dto.WorkflowId, workflow.PublishedVersionId);
                publishedVersion = await _workflowVersionRepository.GetWorkflowVersionAsync(dto.ProjectKey, workflow.PublishedVersionId);
            }
            else
            {
                _logger.LogInformation("No published version found for workflow with Id: {WorkflowId}", dto.WorkflowId);
            }



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

            var workflowDto = new WorkflowResponseDto
            {
                ItemId = workflow.ItemId,
                Name = workflow.Name,
                TenantId = workflow.TenantId,
                Nodes = nodes,
                Edges = workflow.Edges,
                Settings = workflow.Settings ?? new Dictionary<string, string>(),
                IsPublished = workflow.IsPublished,
                Description = workflow.Description,
                NodeOutputSchemas = workflow.NodeOutputSchemas,
                LastUpdatedDate = workflow.LastUpdatedDate,
                CreatedDate = workflow.CreatedDate,
                CreatedBy = workflow.CreatedBy,
                LastUpdatedBy = workflow.LastUpdatedBy,
                IsDirty = workflow.IsDirty,
                PublishedVersion = publishedVersion == null ? null : new WorkflowVersionDto
                {
                    Name = publishedVersion.Name,
                    Description = publishedVersion.Description,
                    VersionId = publishedVersion.ItemId
                }

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
            workflow.Edges = dto.Edges ?? workflow.Edges;
            workflow.NodeOutputSchemas = dto.NodeOutputSchemas ?? workflow.NodeOutputSchemas;
            workflow.IsDirty = true;
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

            await _workflowRepository.UpdateWorkflowAsync(workflow);

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
                await _workflowRepository.DeleteWorkflowAsync(dto.Id, dto.ProjectKey);
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

        public async Task<BaseMutationResponse> CreateVersionAsync(WorkflowVersionCreateRequestDto dto)
        {
            _logger.LogInformation("Creating workflow version for ProjectKey: {ProjectKey}, WorkflowId: {WorkflowId}", dto.ProjectKey, dto.WorkflowId);
            var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);

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
            var snapshot = new WorkflowVersionModel
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
                await _workflowVersionRepository.CreateWorkflowVersionAsync(snapshot);
                _logger.LogInformation("Successfully created workflow version with Id: {SnapshotId} for WorkflowId: {WorkflowId}", snapshot.ItemId, dto.WorkflowId);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = snapshot.ItemId,
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

        public async Task<BaseMutationResponse> UpdateVersionAsync(WorkflowVersionUpdateRequestDto dto)
        {
            _logger.LogInformation("Updating workflow version for ProjectKey: {ProjectKey}, VersionId: {VersionId}", dto.ProjectKey, dto.VersionId);
            var version = await _workflowVersionRepository.GetWorkflowVersionAsync(dto.ProjectKey, dto.VersionId);

            if (version == null)
            {
                _logger.LogWarning("Workflow version with Id {VersionId} not found for update.", dto.VersionId);
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
                await _workflowVersionRepository.UpdateWorkflowVersionAsync(dto.ProjectKey, dto.VersionId, version);
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
                _logger.LogError("Error updating workflow version for VersionId: {VersionId}: {Message}", dto.VersionId, ex.Message);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Failed to update workflow version" } }
                };
            }
        }
        public async Task<WorkflowGetVersionsResponseDto> GetVersionsAsync(WorkflowGetVersionsRequestDto dto)
        {
            try
            {
                var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
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
                _logger.LogInformation("Fetching workflow versions for ProjectKey: {ProjectKey}, WorkflowId: {WorkflowId}", dto.ProjectKey, dto.WorkflowId);
                var versions = await _workflowVersionRepository.GetWorkflowVersionsAsync(dto.ProjectKey, new[] { dto.WorkflowId });
                _logger.LogInformation("Successfully fetched {Count} workflow versions for ProjectKey: {ProjectKey}, WorkflowId: {WorkflowId}", versions.Count, dto.ProjectKey, dto.WorkflowId);

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
                _logger.LogError("Error fetching workflow versions for ProjectKey: {ProjectKey}, WorkflowId: {WorkflowId}: {Message}", dto.ProjectKey, dto.WorkflowId, ex.Message);
                return new WorkflowGetVersionsResponseDto
                {
                    Data = null,
                    TotalCount = 0,
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } },
                };
            }
        }

        public async Task<BaseMutationResponse> PublishNewVersionAsync(WorkflowPublishNewVersionRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
            if (workflow == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }


            var version = new WorkflowVersionModel
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
                _logger.LogInformation("Successfully created workflow version with Id: {VersionId} for WorkflowId: {WorkflowId}", version.ItemId, dto.WorkflowId);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating workflow version for WorkflowId: {WorkflowId}: {Message}", dto.WorkflowId, ex.Message);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow publish failed" } }
                };
            }
            try
            {
                workflow.IsDirty = false;
                workflow.IsPublished = true;
                workflow.PublishedVersionId = version.ItemId;
                workflow.PublishedMeta = new PublishedWorkflowMeta
                {
                    TriggerNodes = workflow.Nodes.Where(n => n.Category == "trigger").ToList()
                };
                await _workflowRepository.UpdateWorkflowAsync(workflow);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = workflow.ItemId,
                    Errors = null
                };
            }
            catch (System.Exception)
            {

                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow publish failed" } }
                };
            }

        }

        public async Task<BaseMutationResponse> PublishVersionAsync(WorkflowPublishVersionRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
            if (workflow == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }

            var version = await _workflowVersionRepository.GetWorkflowVersionAsync(dto.ProjectKey, dto.VersionId);
            if (version == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Version not found" } },
                    ItemId = null
                };
            }

            workflow.IsDirty = false;
            workflow.IsPublished = true;
            workflow.PublishedVersionId = version.ItemId;
            workflow.PublishedMeta = new PublishedWorkflowMeta
            {
                TriggerNodes = version.Snapshot.Nodes.Where(n => n.Category == "trigger").ToList()
            };
            await _workflowRepository.UpdateWorkflowAsync(workflow);

            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = workflow.ItemId,
                Errors = null
            };
        }

        public async Task<BaseMutationResponse> RestoreAsync(WorkflowRestoreRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
            if (workflow == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }

            var restoredVersion = await _workflowVersionRepository.GetWorkflowVersionAsync(dto.ProjectKey, dto.VersionId);
            if (restoredVersion == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Version to restore not found" } },
                    ItemId = null
                };
            }


            var snapshotWorkflow = restoredVersion.Snapshot;
            var updatedWorkflow = snapshotWorkflow;
            updatedWorkflow.ItemId = workflow.ItemId;
            updatedWorkflow.IsDirty = true;
            updatedWorkflow.IsPublished = workflow.IsPublished;
            updatedWorkflow.PublishedVersionId = workflow.PublishedVersionId;
            updatedWorkflow.PublishedMeta = workflow.PublishedMeta;
            updatedWorkflow.LastUpdatedDate = DateTime.UtcNow;
            updatedWorkflow.LastUpdatedBy = BlocksContext.GetContext().UserId ?? "system"; ;
            await _workflowRepository.UpdateWorkflowAsync(updatedWorkflow);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = workflow.ItemId,
                Errors = null
            };
        }

        public async Task<BaseMutationResponse> UnpublishAsync(WorkflowUnpublishRequestDto dto)
        {
            try
            {
                _logger.LogInformation($"Unpublishing workflow workflowId: {dto.WorkflowId}, projectKey: {dto.ProjectKey}");
                var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
                if (workflow == null)
                {
                    _logger.LogWarning("Workflow with Id {WorkflowId} not found for unpublishing.", dto.WorkflowId);
                    return new BaseMutationResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                        ItemId = null
                    };
                }

                workflow.IsDirty = false;
                workflow.IsPublished = false;
                workflow.PublishedVersionId = null;
                workflow.PublishedMeta = null;
                await _workflowRepository.UpdateWorkflowAsync(workflow);

                _logger.LogInformation($"Successfully unpublished workflow. WorkflowId: {workflow.ItemId}");

                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = workflow.ItemId,
                    Errors = null
                };

            }
            catch (Exception ex)
            {
                _logger.LogError("Error unpublishing workflow: {StackTrace}", ex.StackTrace);
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } }
                };
            }
        }

        public async Task<GetWorkflowByVersionResponseDto> GetWorkflowByVersionAsync(GetWorkflowByVersionRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(dto.WorkflowId, dto.ProjectKey);
            var version = await _workflowVersionRepository.GetWorkflowVersionAsync(dto.ProjectKey, dto.VersionId);
            if (version == null)
            {
                return new GetWorkflowByVersionResponseDto
                {
                    IsSuccess = false,
                    data = null,
                    Errors = new Dictionary<string, string> { { "Message", "Version not found" } }
                };
            }
            var snapShot = version.Snapshot;
            var versionedWorkflow = new WorkflowResponseDto
            {
                ItemId = snapShot.ItemId,
                Name = snapShot.Name,
                TenantId = snapShot.TenantId,
                Nodes = snapShot.Nodes.Select(n => new NodeDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Category = n.Category,
                    Type = n.Type,
                    Version = n.Version,
                    Position = n.Position,
                    Parameters = JsonDocument.Parse(n.Parameters.ToJson()).RootElement,
                    Settings = JsonDocument.Parse(n.Settings.ToJson()).RootElement
                }).ToList(),
                Edges = snapShot.Edges,
                Settings = snapShot.Settings ?? new Dictionary<string, string>(),
                IsPublished = workflow.IsPublished,
                Description = snapShot.Description,
                NodeOutputSchemas = snapShot.NodeOutputSchemas,
                LastUpdatedDate = snapShot.LastUpdatedDate,
                CreatedDate = snapShot.CreatedDate,
                CreatedBy = snapShot.CreatedBy,
                LastUpdatedBy = snapShot.LastUpdatedBy,
                IsDirty = snapShot.IsDirty
            };
            return new GetWorkflowByVersionResponseDto
            {
                IsSuccess = true,
                data = versionedWorkflow,
                Errors = null
            };

        }
    }

}
