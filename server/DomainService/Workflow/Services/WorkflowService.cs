using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Utils;
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
        private BaseMutationResponse CreateProjectKeyNotFoundError(string tenantId, string context, string stackTrace)
        {
            _logger.LogError("Error {Context}: {StackTrace}", context, stackTrace);
            return new BaseMutationResponse
            {
                IsSuccess = false,
                ItemId = null,
                Errors = new Dictionary<string, string> { { "Message", $"Project key is not found {tenantId}" } }
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
        private async Task<(WorkflowEntity? workflow, BaseMutationResponse? errorResponse)> TryGetWorkflowAsync(string tenantId, string workflowId, string context)
        {
            try
            {
                var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, workflowId);
                return (workflow, null);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is KeyNotFoundException)
            {
                return (null, CreateProjectKeyNotFoundError(tenantId, context, ex.StackTrace ?? ""));
            }
            catch (Exception ex)
            {
                return (null, CreateGeneralError($"while {context}", ex));
            }
        }

        public async Task<BaseMutationResponse> CreateAsync(string tenantId, WorkflowCreateRequestDto dto)

        {
            _logger.LogInformation($"Creating workflow for ProjectKey: {tenantId}, Name: {dto.Name},");
            var model = new WorkflowEntity
            {
                ItemId = Guid.NewGuid().ToString().Replace("-", ""),
                Name = dto.Name,
                TenantId = tenantId,
                Nodes = JsonConvert.DeserializeObject<List<NodeEntity>>(dto.Nodes.GetRawText()) ?? new(),
                Edges = dto.Edges,
                IsDirty = true,
                IsPublished = false,
                PublishedVersionId = null,
                PublishedMeta = null,
                LastPublishedVersionId = null,
                Settings = dto.Settings,
                Description = dto.Description,
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
                return CreateProjectKeyNotFoundError(tenantId, "inserting workflow", ex.StackTrace ?? "");
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

        public async Task<BaseMutationResponse> DuplicateAsync(string tenantId, WorkflowDuplicateRequestDto dto)
        {
            _logger.LogInformation($"Duplicating workflow tenantId = {tenantId} , WorkflowId = {dto.WorkflowId} and Name={dto.Name}");
            try
            {
                var existingWorkflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
                if (existingWorkflow == null)
                {
                    return new BaseMutationResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                        ItemId = null
                    };
                }
                var newWorkflow = new WorkflowEntity
                {
                    ItemId = Guid.NewGuid().ToString().Replace("-", ""),
                    TenantId = tenantId,
                    Name = dto.Name,
                    Nodes = existingWorkflow.Nodes,
                    Edges = existingWorkflow.Edges,
                    Settings = existingWorkflow.Settings,
                    Description = existingWorkflow.Description,
                    Language = existingWorkflow.Language,
                    OrganizationId = existingWorkflow.OrganizationId,
                    Tags = existingWorkflow.Tags,
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    CreatedBy = BlocksContext.GetContext().UserId ?? "",
                    LastUpdatedBy = BlocksContext.GetContext().UserId ?? ""
                };
                await _workflowRepository.CreateWorkflowAsync(newWorkflow);
                _logger.LogInformation("Successfully duplicated workflow with new Id: {WorkflowId}", existingWorkflow.ItemId);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = newWorkflow.ItemId,
                    Errors = null
                };
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to duplicate workflow error = {e.StackTrace}");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } },
                    ItemId = null
                };
            }
        }
        public async Task<WorkflowGetsResponseDto> GetAllAsync(string tenantId, WorkflowGetsRequestDto dto)
        {
            _logger.LogInformation($"Fetching workflows. ProjectKey: {tenantId}, Page: {dto.PageNumber}, PageSize: {dto.PageSize}, Search: {dto.Search}, IsPublished: {dto.IsPublished}");

            var workflows = await _workflowRepository.GetAllWorkflowsAsync(tenantId, dto.PageSize, dto.PageNumber, dto.Search, dto.IsPublished);
            var totalCount = await _workflowRepository.GetWorkflowsCountAsync(tenantId, dto.Search, dto.IsPublished);

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

            _logger.LogInformation("Completed fetching workflows. ProjectKey: {ProjectKey}, Page: {Page}, PageSize: {PageSize}, Search: {Search}, IsPublished: {IsPublished}", tenantId, dto.PageNumber, dto.PageSize, dto.Search, dto.IsPublished);

            return new WorkflowGetsResponseDto
            {
                Data = workflowDtos,
                TotalCount = totalCount
            };
        }

        public async Task<WorkflowGetResponseDto> GetAsync(string tenantId, WorkflowGetRequestDto dto)
        {
            _logger.LogInformation($"Fetching workflow for ProjectKey: {tenantId}, WorkflowId: {dto.WorkflowId}");
            WorkflowEntity workflow;
            try
            {
                workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is KeyNotFoundException)
            {
                _logger.LogError("Error fetching workflow: {StackTrace}", ex.StackTrace);
                return new WorkflowGetResponseDto
                {
                    IsSuccess = false,
                    data = null,
                    Errors = new Dictionary<string, string> { { "Message", $"Project key is not found {tenantId}" } }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while fetching workflow: {StackTrace}", ex.StackTrace);
                return new WorkflowGetResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } },
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

            WorkflowVersionEntity publishedVersion = null;
            if (workflow.IsPublished && !string.IsNullOrEmpty(workflow.PublishedVersionId))
            {
                _logger.LogInformation("Fetching published version for workflow with Id: {WorkflowId}, PublishedVersionId: {PublishedVersionId}", dto.WorkflowId, workflow.PublishedVersionId);
                publishedVersion = await _workflowVersionRepository.GetWorkflowVersionAsync(tenantId, workflow.PublishedVersionId);
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
                Handle = n.Handle,
                Parameters = BsonJsonConverter.ToJsonElement(n.Parameters),
                Settings = BsonJsonConverter.ToJsonElement(n.Settings),
                PinData = BsonJsonConverter.ToJsonElementOrNull(n.PinData),
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


        public async Task<BaseMutationResponse> UpdateAsync(string tenantId, WorkflowUpdateRequestDto dto)
        {
            _logger.LogInformation("Updating workflow with Dto: {Dto}", JsonConvert.SerializeObject(dto));

            var (workflow, errorResponse) = await TryGetWorkflowAsync(tenantId, dto.ItemId, "updating workflow");
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
            workflow.IsDirty = true;
            if (dto.Nodes != null)
            {
                workflow.Nodes = dto.Nodes.Select(n => new NodeEntity
                {
                    Name = n.Name,
                    Id = n.Id,
                    Category = n.Category,
                    Type = n.Type,
                    Version = n.Version,
                    Position = n.Position,
                    Handle = n.Handle,
                    Parameters = BsonDocument.Parse(n.Parameters.GetRawText()),
                    Settings = BsonDocument.Parse(n.Settings.GetRawText()),
                    PinData = BsonJsonConverter.ToBsonArrayOrNull(n.PinData),
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



        public async Task<BaseMutationResponse> DeleteAsync(string tenantId, WorkflowDeleteRequestDto dto)
        {
            _logger.LogInformation($"Deleting workflow tenantId: {tenantId} and workflowId = {dto.Id}");
            try
            {
                var existingWorkflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.Id);
                if (existingWorkflow == null)
                {
                    _logger.LogError($"Workflow not found {dto.Id}");
                    return new BaseMutationResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } }
                    };
                }
                await _workflowRepository.DeleteWorkflowAsync(tenantId, dto.Id);
                await _workflowVersionRepository.DeleteWorkflowVersionsByWorkflowIdAsync(tenantId, dto.Id);
                _logger.LogInformation($"Deleted Workflow successfully, workflowId = ${dto.Id}");
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = existingWorkflow.ItemId,

                };

            }
            catch (Exception e)
            {
                _logger.LogError($"Error deleting workflow: {e.StackTrace}");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Something went wrong" } }
                };
            }
        }


        public async Task<BaseMutationResponse> PublishNewVersionAsync(string tenantId, WorkflowPublishNewVersionRequestDto dto)
        {
            _logger.LogInformation($"Start Publish New Version for TenantId = {tenantId} and workflowId = {dto.WorkflowId}");
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            if (workflow == null)
            {
                _logger.LogError($"Workflow not found");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
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
                workflow.LastPublishedVersionId = workflow.PublishedVersionId; // Store the previous published version ID
                workflow.PublishedMeta = new PublishedWorkflowMeta
                {
                    TriggerNodes = workflow.Nodes.Where(n => n.Category == "trigger").ToList()
                };
                await _workflowRepository.UpdateWorkflowAsync(workflow);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = version.ItemId,
                    Errors = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Workflow publish failed, error = {ex.StackTrace}");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    ItemId = null,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow publish failed" } }
                };
            }

        }

        public async Task<BaseMutationResponse> PublishVersionAsync(string tenantId, WorkflowPublishVersionRequestDto dto)
        {
            _logger.LogInformation($"Start Publishing a version for tenantId = {tenantId}, workflowId = {dto.WorkflowId} and VersionId = {dto.VersionId}");
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            if (workflow == null)
            {
                _logger.LogError("Workflow not found");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }

            // if the versionId is null or empty, then publish the last published version
            if (string.IsNullOrEmpty(dto.VersionId))
            {
                if (string.IsNullOrEmpty(workflow.LastPublishedVersionId))
                {
                    return new BaseMutationResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string> { { "Message", "No version to publish" } },
                        ItemId = null
                    };
                }
                dto.VersionId = workflow.LastPublishedVersionId;
            }
            _logger.LogInformation($"Fetch Verion");
            var version = await _workflowVersionRepository.GetWorkflowVersionAsync(tenantId, dto.VersionId);
            if (version == null)
            {
                _logger.LogError("Verion not found");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Version not found" } },
                    ItemId = null
                };
            }

            workflow.IsDirty = false;
            workflow.IsPublished = true;
            workflow.LastPublishedVersionId = workflow.PublishedVersionId; // Store the previous published version ID
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

        public async Task<BaseMutationResponse> RestoreAsync(string tenantId, WorkflowRestoreRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            if (workflow == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }

            var restoredVersion = await _workflowVersionRepository.GetWorkflowVersionAsync(tenantId, dto.VersionId);
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
            updatedWorkflow.LastPublishedVersionId = workflow.LastPublishedVersionId;
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

        public async Task<BaseMutationResponse> UnpublishAsync(string tenantId, WorkflowUnpublishRequestDto dto)
        {
            try
            {
                _logger.LogInformation($"Unpublishing workflow workflowId: {dto.WorkflowId}, projectKey: {tenantId}");
                var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
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

        public async Task<GetWorkflowByVersionResponseDto> GetWorkflowByVersionAsync(string tenantId, GetWorkflowByVersionRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            var version = await _workflowVersionRepository.GetWorkflowVersionAsync(tenantId, dto.VersionId);
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
                    Handle = n.Handle,
                    Parameters = BsonJsonConverter.ToJsonElement(n.Parameters),
                    Settings = BsonJsonConverter.ToJsonElement(n.Settings),
                    PinData = BsonJsonConverter.ToJsonElementOrNull(n.PinData),
                }).ToList(),
                Edges = snapShot.Edges,
                Settings = snapShot.Settings ?? new Dictionary<string, string>(),
                IsPublished = workflow.IsPublished,
                Description = snapShot.Description,
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

        public async Task<BaseMutationResponse> TriggerListenerAsync(string tenantId, TriggerListenerRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            if (workflow == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Workflow not found" } },
                    ItemId = null
                };
            }
            if (!dto.EnableListener)
            {
                workflow.TestMeta = null;
                await _workflowRepository.UpdateWorkflowAsync(workflow);
                return new BaseMutationResponse
                {
                    IsSuccess = true,
                    ItemId = workflow.ItemId,
                    Errors = null
                };
            }
            if (String.IsNullOrEmpty(dto.TriggerId))
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "TriggerId is required when enabling listener" } },
                    ItemId = null
                };
            }
            var triggerNode = workflow.Nodes.FirstOrDefault(n => n.Id == dto.TriggerId && n.Category == "trigger");
            if (triggerNode == null)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Trigger node not found" } },
                    ItemId = null
                };
            }
            if (!String.IsNullOrWhiteSpace(dto.CompletionNodeId))
            {
                var completionNode = workflow.Nodes.FirstOrDefault(n => n.Id == dto.CompletionNodeId);
                if (completionNode == null)
                {
                    return new BaseMutationResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string> { { "Message", "Completion node not found" } },
                        ItemId = null
                    };
                }
            }
            workflow.TestMeta = new TestWorkflowMeta
            {
                ListenerTriggerNodes = new List<NodeEntity> { triggerNode },
                UserIds = new List<string> { BlocksContext.GetContext().UserId ?? "system" },
                IsListening = true,
                CompletionNodeId = dto.CompletionNodeId ?? null,

            };
            await _workflowRepository.UpdateWorkflowAsync(workflow);
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = workflow.ItemId,
                Errors = null
            };

        }
    }

}
