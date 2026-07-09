using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis;
using DomainService.Workflow.Models;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Events;
using DomainService.Workflow.Enums;
using DomainService.Workflow.Utils;
using Microsoft.Extensions.Logging;
using DomainService.Workflow.Nodes.TriggerEmailV1;
using DomainService.Workflow.Nodes.TriggerDataV1;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using DotLiquid.Util;



namespace DomainService.Workflow.Services
{
    [ExcludeFromCodeCoverage]
    public class WorkflowExecutionService : IWorkflowExecutionService
    {
        private readonly IWorkflowRepository _workflowRepository;
        private readonly IWorkflowExecutionRepository _executionRepository;
        private readonly IWorkflowVersionRepository _workflowVersionRepository;

        private readonly IMessageClient _messageClient;

        private readonly ILogger<WorkflowExecutionService> _logger;
        private readonly IWorkflowEngineService _workflowEngineService;
        private readonly IWorkflowNotificationService _workflowNotificationService;

        public WorkflowExecutionService(
            IWorkflowRepository workflowRepository,
            IWorkflowExecutionRepository executionRepository,
            IMessageClient messageClient,
            ILogger<WorkflowExecutionService> logger,
            IWorkflowEngineService workflowEngineService,
            IWorkflowVersionRepository workflowVersionRepository,
            IWorkflowNotificationService workflowNotificationService
            )
        {
            _workflowRepository = workflowRepository;
            _executionRepository = executionRepository;
            _messageClient = messageClient;
            _workflowEngineService = workflowEngineService;
            _logger = logger;
            _workflowVersionRepository = workflowVersionRepository;
            _workflowNotificationService = workflowNotificationService;
        }

        private async Task NotifyWorkflowStartedAsync(WorkflowExecutionModel execution)
        {
            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                nodeExecution: null,
                eventName: "WorkflowStarted",
                code: ExecutionEventCodes.WorkflowExecutionCode(WorkflowExecutionStatus.Running),
                status: nameof(WorkflowExecutionStatus.Running),
                data: execution.Id!,
                message: $"Workflow '{execution.WorkflowSnapshot.Name}' started.");
        }


        public async Task<WorkflowExecutionModel> CreateExecutionAsync(WorkflowModel workflowSnapshot, TriggerMetadata triggerMetadata, WorkflowExecutionMode executionMode = WorkflowExecutionMode.Test)
        {
            var execution = new WorkflowExecutionModel
            {
                Id = Guid.NewGuid().ToString().Replace("-", ""),
                WorkflowId = workflowSnapshot.ItemId,
                WorkflowName = workflowSnapshot.Name,
                TenantId = workflowSnapshot.TenantId,
                WorkflowSnapshot = workflowSnapshot,
                Status = WorkflowExecutionStatus.Init,
                ExecutionMode = executionMode,
                TriggerMetadata = triggerMetadata,
                NodeExecutions = new List<NodeExecutionModel>(),
                StartedAt = DateTime.UtcNow,
            };
            return await _executionRepository.CreateAsync(execution);
        }

        public async Task<WorkflowWebhookResponseDto> TriggerWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, workflowId);
            if (workflow == null)
            {
                _logger.LogError("Workflow not found: {WorkflowId}, {TenantId}", workflowId, tenantId);
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = "Workflow not found"
                };
            }
            if (!workflow.IsPublished)
            {
                _logger.LogError("Workflow is not published: {WorkflowId}, {TenantId}", workflowId, tenantId);
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = "Workflow is not published"
                };
            }
            if (String.IsNullOrWhiteSpace(workflow.PublishedVersionId))
            {
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = "Workflow is not published"
                };
            }
            var publishedVersion = await _workflowVersionRepository.GetWorkflowVersionAsync(tenantId, workflow.PublishedVersionId);

            if (publishedVersion == null)
            {
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = "Something went wrong"
                };
            }

            var workfowSnapshot = publishedVersion.Snapshot;
            if (workfowSnapshot == null)
            {
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = "Something went wrong"
                };
            }
            return await HandleWebhookExecutionAsync(workfowSnapshot, WorkflowExecutionMode.Production, triggerId, input);
        }

        public async Task<WorkflowWebhookResponseDto> TriggerTestWebhookAsync(string workflowId, string triggerId, string tenantId, JsonElement input)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, workflowId);
            if (workflow == null)
            {
                _logger.LogError("Workflow not found: {WorkflowId}, {TenantId}", workflowId, tenantId);
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = "Workflow not found"
                };
            }
            if (workflow.TestMeta == null || !workflow.TestMeta.IsListening || workflow.TestMeta.ListenerTriggerNodes == null || !workflow.TestMeta.ListenerTriggerNodes.Any(n => n.Id == triggerId))
            {
                _logger.LogError("Workflow is not active for testing: {WorkflowId}, {TenantId}", workflowId, tenantId);
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = null,
                    Status = $"The requested webhook {triggerId} is not listening for test executions. Please activate the test mode in the workflow",
                };
            }
            return await HandleWebhookExecutionAsync(workflow, WorkflowExecutionMode.Test, triggerId, input);
        }
        private async Task<WorkflowWebhookResponseDto?> HandleWebhookExecutionAsync(WorkflowModel workflow, WorkflowExecutionMode executionMode, string triggerId, JsonElement input)
        {
            WorkflowExecutionModel execution;
            try
            {

                var triggerNode = workflow.Nodes.FirstOrDefault(n => n.Id == triggerId);
                if (triggerNode == null)
                {
                    _logger.LogError("Trigger node {TriggerId} not found in workflow {WorkflowId}", triggerId, workflow.ItemId);
                    return new WorkflowWebhookResponseDto
                    {
                        ExecutionId = null,
                        Status = "Workflow not found",
                    };
                }
                var authType = triggerNode.Parameters.GetValue("authType");

                if (authType != null && authType.ToString().ToLower() == "blocksAccessToken".ToLower())
                {
                    var blocksContext = BlocksContext.GetContext();
                    if (!blocksContext.IsAuthenticated) throw new UnauthorizedAccessException();

                }

                var normalizedInput = new BsonArray();

                if (input.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in input.EnumerateArray())
                    {
                        normalizedInput.Add(BsonDocument.Parse(element.GetRawText()));
                    }
                }
                else
                {
                    normalizedInput.Add(BsonDocument.Parse(input.GetRawText()));
                }

                var triggerMetadata = new TriggerMetadata
                {
                    TriggerNodeId = triggerId,
                    TriggerType = triggerNode.Type,
                    TriggerData = normalizedInput,
                };

                execution = await CreateExecutionAsync(workflow, triggerMetadata, executionMode);
                execution.Context["Input"] = normalizedInput;
                execution.Status = WorkflowExecutionStatus.Queued;
                execution.ActiveNodeIds.Add(triggerId);

                await _executionRepository.UpdateAsync(execution);
                await NotifyWorkflowStartedAsync(execution);

                var payload = new AddExcuationNodeEvent
                {
                    WorkflowId = workflow.ItemId,
                    WorkflowExecutionId = execution.Id,
                    NodeId = triggerId,
                    ProjectKey = workflow.TenantId,
                };

                var responseMode = triggerNode.Parameters.GetValue("httpResponseMode");

                if (responseMode != null && responseMode.ToString().ToLower() == "last")
                {
                    var response = await _workflowEngineService.RunNodeInProcessAsync(payload);
                    var responseModeData = triggerNode.Parameters.GetValue("httpResponseData");
                    if (responseModeData == null)
                    {
                        return new WorkflowWebhookResponseDto
                        {
                            ExecutionId = execution.Id,
                            Status = "Completed",
                        };
                    }
                    if (responseModeData.ToString().ToLower() == "none")
                    {

                        return null;
                    }
                    var lastExecutationNode = response.NodeExecutions.MaxBy(ne => ne.RunIndex);
                    var lastNodeOutput = await _executionRepository.GetAllItemsByNodeExecutionIdAsync(lastExecutationNode.Id, workflow.TenantId);
                    var data = JsonDocument.Parse(new BsonArray(lastNodeOutput.Select(item => item.Data.Output)).ToJson()).RootElement;

                    if (responseModeData.ToString().ToLower() == "all")
                    {
                        return new WorkflowWebhookResponseDto
                        {
                            ExecutionId = execution.Id,
                            Status = "Completed",
                            Data = data
                        };
                    }
                    return new WorkflowWebhookResponseDto
                    {
                        ExecutionId = execution.Id,
                        Status = "Completed",
                        Data = data[0]
                    };
                }
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<AddExcuationNodeEvent>
                {
                    ConsumerName = LogicConstants.NodeExecutionQueue,
                    Payload = payload
                });
                return new WorkflowWebhookResponseDto
                {
                    ExecutionId = execution.Id,
                    Status = "Queued"
                };
            }
            catch (UnauthorizedAccessException)
            {
                throw; // preserve 401
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to create execution for webhook", ex);
            }
        }


        public async Task EmailTriggerStartAsync(EmailTriggerEvent emailEvent)
        {
            _logger.LogInformation("Starting EmailTriggerStartAsync for MailServerConfigurationId: {MailServerConfigurationId} and status: {Status}", emailEvent.Mail.MailServerConfigurationId, emailEvent.Mail.Status);
            var tenantId = emailEvent.ProjectKey;
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogError("ProjectKey (TenantId) is null or empty in BlocksContext");
                return;

            }
            if (emailEvent.Mail.MailServerConfigurationId == null)
            {
                _logger.LogError("MailServerConfigurationId is null in EmailTriggerEvent");
                return;
            }

            if (emailEvent.Type != EmailTriggerType.Inbound)
            {
                _logger.LogInformation("EmailTriggerType is {EmailTriggerType}, skipping processing.", emailEvent.Type);
                return;
            }

            if (emailEvent.Mail.Status != MailStatus.Received)
            {
                _logger.LogInformation("Email status is {MailStatus}, skipping processing.", emailEvent.Mail.Status);
                return;
            }

            var workflows = await _workflowRepository.GetWorkflowsByMailServerConfigurationIdAsync(tenantId, emailEvent.Mail.MailServerConfigurationId);
            _logger.LogInformation("Found {WorkflowCount} workflows for MailServerConfigurationId: {MailServerConfigurationId}", workflows.Count, emailEvent.Mail.MailServerConfigurationId);

            foreach (var workflow in workflows)
            {
                try
                {
                    _logger.LogInformation("Creating execution for WorkflowId: {WorkflowId}", workflow.ItemId);
                    var triggerNode = workflow.Nodes.FirstOrDefault(n =>
                        n.Type == "email" &&
                        n.Category == "trigger" &&
                        n.Parameters != null &&
                        n.Parameters.Contains("mailServerConfigurationId") &&
                        n.Parameters["mailServerConfigurationId"].ToString() == emailEvent.Mail.MailServerConfigurationId
                    );

                    if (triggerNode == null)
                    {
                        _logger.LogWarning("No Email trigger node found for WorkflowId: {WorkflowId} with MailServerConfigurationId: {MailServerConfigurationId}", workflow.ItemId, emailEvent.Mail.MailServerConfigurationId);
                        continue;
                    }

                    var execution = await CreateExecutionAsync(workflow, new TriggerMetadata
                    {
                        TriggerNodeId = triggerNode.Id,
                        TriggerType = triggerNode.Type,
                        TriggerData = new BsonArray { BsonDocument.Parse(JsonSerializer.Serialize(emailEvent.Mail)) }
                    }, WorkflowExecutionMode.Production);
                    var emailJson = JsonSerializer.Serialize(emailEvent.Mail);
                    var emailBsonDoc = BsonDocument.Parse(emailJson);
                    execution.Context["Input"] = new BsonArray { emailBsonDoc };

                    execution.Status = WorkflowExecutionStatus.Queued;
                    execution.ActiveNodeIds.Add(triggerNode.Id);

                    await _executionRepository.UpdateAsync(execution);
                    await NotifyWorkflowStartedAsync(execution);
                    await _messageClient.SendToConsumerAsync(new ConsumerMessage<AddExcuationNodeEvent>
                    {
                        ConsumerName = LogicConstants.NodeExecutionQueue,
                        Payload = new AddExcuationNodeEvent
                        {
                            WorkflowId = workflow.ItemId,
                            WorkflowExecutionId = execution.Id!,
                            NodeId = triggerNode.Id,
                            ProjectKey = tenantId
                        }
                    });
                    _logger.LogInformation("Queued execution {ExecutionId} for WorkflowId: {WorkflowId}", execution.Id, workflow.ItemId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create execution for WorkflowId: {WorkflowId}", workflow.ItemId);
                }
            }
        }

        public async Task<WorkflowExecutionsGetResponseDto> GetExecutionsByWorkflowIdAsync(string projectKey, WorkflowExecutionsGetRequestDto dto)
        {
            var executions = await _executionRepository.GetByWorkflowIdAsync(dto.WorkflowId, projectKey);

            var executionItems = executions.Select(e => new WorkflowExecutionItemDto
            {
                Id = e.Id!,
                WorkflowId = e.WorkflowId,
                WorkflowName = e.WorkflowName,
                Status = e.Status,
                StartedAt = e.StartedAt,
                FinishedAt = e.FinishedAt,
                ErrorMessage = e.ErrorMessage,
                AttemptNumber = e.AttemptNumber,
                ExecutionMode = e.ExecutionMode,
                // TriggerMetadata = e.TriggerMetadata
            }).ToList();

            return new WorkflowExecutionsGetResponseDto
            {
                TotalCount = executionItems.Count,
                Data = executionItems,
                Errors = null
            };
        }

        public async Task<WorkflowExecutionGetResponseDto> GetExecutionByIdAsync(string projectKey, WorkflowExecutionGetRequestDto dto)
        {
            var execution = await _executionRepository.GetByIdAsync(dto.ExecutionId, projectKey)
                ?? throw new InvalidOperationException($"Execution {dto.ExecutionId} not found");


            if (execution == null)
            {
                return new WorkflowExecutionGetResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Execution not found" } }
                };
            }

            // Get all workflow items - frontend will organize them into input/output structure
            var allItems = await _executionRepository.GetAllItemsByExecutionIdAsync(dto.ExecutionId, projectKey);

            var nodes = execution.WorkflowSnapshot.Nodes.Select(item => new NodeDto
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.Type,
                Version = item.Version,
                Category = item.Category,
                Position = item.Position,
                Handle = item.Handle,
                Parameters = BsonJsonConverter.ToJsonElement(item.Parameters),
                Settings = BsonJsonConverter.ToJsonElement(item.Settings),
                PinData = BsonJsonConverter.ToJsonElementOrNull(item.PinData)
            }).ToList();

            var workflow = new WorkflowResponseDto
            {
                ItemId = execution.WorkflowSnapshot.ItemId,
                Name = execution.WorkflowSnapshot.Name,
                Nodes = nodes,
                Edges = execution.WorkflowSnapshot.Edges,
                IsPublished = execution.WorkflowSnapshot.IsPublished,
                Settings = execution.WorkflowSnapshot.Settings,
                TenantId = execution.WorkflowSnapshot.TenantId
            };



            return new WorkflowExecutionGetResponseDto
            {
                IsSuccess = true,
                Data = new WorkflowExecutionDto
                {
                    Id = execution.Id,
                    WorkflowId = execution.WorkflowId,
                    WorkflowName = execution.WorkflowName,
                    Status = execution.Status,
                    ExecutionMode = execution.ExecutionMode,
                    StartedAt = execution.StartedAt,
                    FinishedAt = execution.FinishedAt,
                    ErrorMessage = execution.ErrorMessage,
                    AttemptNumber = execution.AttemptNumber,
                    Context = BsonJsonConverter.ToJsonElement(execution.Context),
                    ActiveNodeIds = execution.ActiveNodeIds,
                    // TriggerMetadata = execution.TriggerMetadata,
                    NodeExecutions = execution.NodeExecutions.Select(ne => new NodeExecutionResponseDto
                    {
                        Id = ne.Id,
                        NodeId = ne.NodeId,
                        NodeName = ne.NodeName,
                        NodeType = ne.NodeType,
                        NodeVersion = ne.NodeVersion,
                        RunIndex = ne.RunIndex,
                        Status = ne.Status,
                        InputItemCount = ne.InputItemCount,
                        OutputItemCount = ne.OutputItemCount,
                        OutputCountsByBranch = ne.OutputCountsByBranch,
                        StartedAt = ne.StartedAt,
                        EndedAt = ne.EndedAt,
                        Error = ne.Error,
                        AttemptNumber = ne.AttemptNumber,
                        Parameters = JsonDocument.Parse(workflow.Nodes.FirstOrDefault(n => n.Id == ne.NodeId)?.Parameters.ToJson() ?? "{}"),
                        Input = new JsonArray(
                            allItems
                                .Where(p => allItems
                                    .Where(i => i.NodeExecutionId == ne.Id)
                                    .SelectMany(i => i.ParentItemIds ?? new List<string>())
                                    .Contains(p.Id))
                                .Select(p => JsonNode.Parse(p.Data.Output?.ToJson() ?? "null"))
                                .ToArray()),
                        Output = new JsonArray(
                            allItems
                                .Where(i => i.NodeExecutionId == ne.Id)
                                .Select(i => JsonNode.Parse(i.Data.Output?.ToJson() ?? "null"))
                                .ToArray()),
                    }).ToList(),
                    WorkflowSnapshot = workflow,
                    Items = allItems.Select(i => new WorkflowItemExecutionDto
                    {
                        ItemId = i.Id!,
                        NodeId = i.NodeId,
                        NodeExecutionId = i.NodeExecutionId,
                        Branch = i.Branch,
                        Data = JsonDocument.Parse(i.Data.ToJson()),
                        ParentItemIds = i.ParentItemIds,
                        ItemIndex = i.ItemIndex,
                        CreatedAt = i.CreatedAt
                    }).ToList()
                }
            };
        }

        public async Task<WorkflowExecutionGetResponseDto> LastSuccessfullExecutionAsync(string projectKey, LastSuccessfullExecutionRequestDto dto)
        {
            var execution = await _executionRepository.GetLastCompletedExecution(projectKey, dto.WorkflowId);
            if (execution == null)
            {
                return new WorkflowExecutionGetResponseDto
                {
                    IsSuccess = false,
                    Data = null,
                    Errors = new Dictionary<string, string> { { "Message", "No Execution" } }
                };
            }
            var allItems = await _executionRepository.GetAllItemsByExecutionIdAsync(execution.Id, projectKey);

            var nodes = execution.WorkflowSnapshot.Nodes.Select(item => new NodeDto
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.Type,
                Version = item.Version,
                Category = item.Category,
                Position = item.Position,
                Handle = item.Handle,
                Parameters = BsonJsonConverter.ToJsonElement(item.Parameters),
                Settings = BsonJsonConverter.ToJsonElement(item.Settings),
                PinData = BsonJsonConverter.ToJsonElementOrNull(item.PinData)
            }).ToList();

            var workflow = new WorkflowResponseDto
            {
                ItemId = execution.WorkflowSnapshot.ItemId,
                Name = execution.WorkflowSnapshot.Name,
                Nodes = nodes,
                Edges = execution.WorkflowSnapshot.Edges,
                IsPublished = execution.WorkflowSnapshot.IsPublished,
                Settings = execution.WorkflowSnapshot.Settings,
                TenantId = execution.WorkflowSnapshot.TenantId
            };



            return new WorkflowExecutionGetResponseDto
            {
                IsSuccess = true,
                Data = new WorkflowExecutionDto
                {
                    Id = execution.Id,
                    WorkflowId = execution.WorkflowId,
                    WorkflowName = execution.WorkflowName,
                    Status = execution.Status,
                    ExecutionMode = execution.ExecutionMode,
                    StartedAt = execution.StartedAt,
                    FinishedAt = execution.FinishedAt,
                    ErrorMessage = execution.ErrorMessage,
                    // TriggerMetadata = execution.TriggerMetadata,
                    AttemptNumber = execution.AttemptNumber,
                    Context = BsonJsonConverter.ToJsonElement(execution.Context),
                    ActiveNodeIds = execution.ActiveNodeIds,
                    NodeExecutions = execution.NodeExecutions.Select(ne => new NodeExecutionResponseDto
                    {
                        Id = ne.Id,
                        NodeId = ne.NodeId,
                        NodeName = ne.NodeName,
                        NodeType = ne.NodeType,
                        NodeVersion = ne.NodeVersion,
                        RunIndex = ne.RunIndex,
                        Status = ne.Status,
                        InputItemCount = ne.InputItemCount,
                        OutputItemCount = ne.OutputItemCount,
                        OutputCountsByBranch = ne.OutputCountsByBranch,
                        StartedAt = ne.StartedAt,
                        EndedAt = ne.EndedAt,
                        Error = ne.Error,
                        AttemptNumber = ne.AttemptNumber,
                        Parameters = JsonDocument.Parse(workflow.Nodes.FirstOrDefault(n => n.Id == ne.NodeId)?.Parameters.ToJson() ?? "{}"),
                        Input = new JsonArray(
                            allItems
                                .Where(p => allItems
                                    .Where(i => i.NodeExecutionId == ne.Id)
                                    .SelectMany(i => i.ParentItemIds ?? new List<string>())
                                    .Contains(p.Id))
                                .Select(p => JsonNode.Parse(p.Data.Output?.ToJson() ?? "null"))
                                .ToArray()),
                        Output = new JsonArray(
                            allItems
                                .Where(i => i.NodeExecutionId == ne.Id)
                                .Select(i => JsonNode.Parse(i.Data.Output?.ToJson() ?? "null"))
                                .ToArray()),
                    }).ToList(),
                    WorkflowSnapshot = workflow,
                    Items = allItems.Select(i => new WorkflowItemExecutionDto
                    {
                        ItemId = i.Id!,
                        NodeId = i.NodeId,
                        NodeExecutionId = i.NodeExecutionId,
                        Branch = i.Branch,
                        Data = JsonDocument.Parse(i.Data.ToJson()),
                        ParentItemIds = i.ParentItemIds,
                        ItemIndex = i.ItemIndex,
                        CreatedAt = i.CreatedAt
                    }).ToList()
                }
            };
        }
        public async Task DataTriggerStartAsync(DataChangeEvent dataEvent)
        {
            _logger.LogInformation("Starting DataTriggerStartAsync for Collection: {CollectionName}, Operation: {Operation}",
                dataEvent.CollectionName, dataEvent.Operation);

            var tenantId = dataEvent.ProjectKey;
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogError("ProjectKey (TenantId) is null or empty in BlocksContext");
                return;
            }

            var operationStr = dataEvent.Operation.ToString();
            var triggerData = BuildTriggerData(dataEvent, operationStr);
            var isMockedData = triggerData.All(data => data.AsBsonDocument.Contains("Tags") && data.AsBsonDocument
              ["Tags"].AsBsonArray.Contains("mock-data"));

            List<WorkflowModel> workflows = new List<WorkflowModel>();
            // If the data is mocked, we will use the workflow as is, otherwise we will only published workflows that are published.
            if (isMockedData)
            {
                workflows = await _workflowRepository.GetWorkflowsByDataCollectionAsync(tenantId, dataEvent.CollectionName, operationStr);
            }
            else
            {
                var publishedWorkflows = await _workflowRepository.GetPublishWorkflowsByDataCollectionAsync(tenantId, dataEvent.CollectionName, operationStr);
                var publishedWorkflowsId = publishedWorkflows.Where(item => item.IsPublished).Select(w => w.ItemId).ToList();

                var versions = await _workflowVersionRepository.GetWorkflowVersionsAsync(tenantId, publishedWorkflowsId.ToArray());
                var publishedVersionIds = publishedWorkflows.Select(item => item.PublishedVersionId).ToList();
                workflows = versions.Where(v => publishedVersionIds.Contains(v.ItemId)).Select(item => item.Snapshot).ToList();
            }



            _logger.LogInformation("Found {Count} workflows for Collection: {CollectionName}, Operation: {Operation}",
                workflows.Count, dataEvent.CollectionName, operationStr);



            foreach (var workflow in workflows)
            {
                var executionMode = isMockedData ? WorkflowExecutionMode.Test : WorkflowExecutionMode.Production;
                await QueueDataTriggerExecutionAsync(workflow, executionMode, dataEvent, operationStr, triggerData, tenantId);
            }
        }

        public static BsonArray BuildTriggerData(DataChangeEvent dataEvent, string operationStr)
        {
            var timestamp = dataEvent.Timestamp.ToString("o");
            var results = new BsonArray();

            if (dataEvent.Operation == DataChangeOperation.Updated && dataEvent.UpdatedDocuments != null)
            {
                foreach (var updatedDoc in dataEvent.UpdatedDocuments)
                {
                    var doc = new BsonDocument
                    {
                        { "Operation", operationStr },
                        { "CollectionName", dataEvent.CollectionName },
                        { "SchemaName", dataEvent.SchemaName },
                        { "DocumentId", updatedDoc.DocumentId },
                        { "Timestamp", timestamp },
                        { "UpdatedFields", new BsonArray(updatedDoc.UpdatedFields.Select(field => new BsonDocument
                            {
                                { "FieldName", field.FieldName },
                                { "OldValue", field.OldValue?.ToString() ?? "" },
                                { "NewValue", field.NewValue?.ToString() ?? "" }
                            }))
                        }
                    };
                    results.Add(doc);
                }
            }
            else if (dataEvent.Data != null && dataEvent.Data.Count > 0)
            {
                foreach (var dataDoc in dataEvent.Data)
                {
                    var doc = new BsonDocument
                    {
                        { "Operation", operationStr },
                        { "CollectionName", dataEvent.CollectionName },
                        { "SchemaName", dataEvent.SchemaName },
                        { "Timestamp", timestamp }
                    };

                    var bsonDoc = DictionaryToBsonDocument(dataDoc);
                    if (bsonDoc.Contains("_id"))
                    {
                        doc["DocumentId"] = bsonDoc["_id"].ToString()!;
                        bsonDoc.Remove("_id");
                    }

                    foreach (var element in bsonDoc)
                    {
                        doc[element.Name] = element.Value;
                    }

                    results.Add(doc);
                }
            }

            if (results.Count == 0)
            {
                results.Add(new BsonDocument
                {
                    { "Operation", operationStr },
                    { "CollectionName", dataEvent.CollectionName },
                    { "SchemaName", dataEvent.SchemaName },
                    { "Timestamp", timestamp }
                });
            }

            return results;
        }

        private async Task QueueDataTriggerExecutionAsync(
            WorkflowModel workflow, WorkflowExecutionMode executionMode, DataChangeEvent dataEvent, string operationStr,
            BsonArray triggerData, string projectKey)
        {
            try
            {
                var triggerNode = workflow.Nodes.FirstOrDefault(n =>
                    n.Type == "dataGateway" &&
                    n.Category == "trigger" &&
                    n.Parameters != null &&
                    n.Parameters.Contains("collectionName") &&
                    n.Parameters["collectionName"].ToString() == dataEvent.CollectionName &&
                    n.Parameters.Contains("operation") &&
                    n.Parameters["operation"].ToString() == operationStr
                );

                if (triggerNode == null)
                {
                    _logger.LogWarning("No Data trigger node found for WorkflowId: {WorkflowId} with Collection: {CollectionName}",
                        workflow.ItemId, dataEvent.CollectionName);
                    return;
                }

                var triggerMetadata = new TriggerMetadata
                {
                    TriggerNodeId = triggerNode.Id,
                    TriggerType = triggerNode.Type,
                    TriggerData = triggerData
                };
                var execution = await CreateExecutionAsync(workflow, triggerMetadata, executionMode);
                execution.Context["Input"] = triggerData;
                execution.Status = WorkflowExecutionStatus.Queued;
                execution.ActiveNodeIds.Add(triggerNode.Id);

                await _executionRepository.UpdateAsync(execution);
                await NotifyWorkflowStartedAsync(execution);
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<AddExcuationNodeEvent>
                {
                    ConsumerName = LogicConstants.NodeExecutionQueue,
                    Payload = new AddExcuationNodeEvent
                    {
                        WorkflowId = workflow.ItemId,
                        WorkflowExecutionId = execution.Id!,
                        NodeId = triggerNode.Id,
                        ProjectKey = projectKey
                    }
                });

                _logger.LogInformation("Queued execution {ExecutionId} for WorkflowId: {WorkflowId} (Data Trigger)",
                    execution.Id, workflow.ItemId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create execution for WorkflowId: {WorkflowId} (Data Trigger)",
                    workflow.ItemId);
            }
        }



        private static BsonDocument DictionaryToBsonDocument(Dictionary<string, object?> dict)
        {
            var doc = new BsonDocument();
            foreach (var kv in dict)
            {
                doc[kv.Key] = ObjectToBsonValue(kv.Value);
            }
            return doc;
        }

        private static BsonValue ObjectToBsonValue(object? value)
        {
            if (value is null) return BsonNull.Value;

            if (value is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.String => new BsonString(je.GetString()),
                    JsonValueKind.Number => je.TryGetInt64(out var l) ? new BsonInt64(l) : new BsonDouble(je.GetDouble()),
                    JsonValueKind.True => new BsonBoolean(true),
                    JsonValueKind.False => new BsonBoolean(false),
                    JsonValueKind.Null => BsonNull.Value,
                    JsonValueKind.Object => JsonElementToBsonDocument(je),
                    JsonValueKind.Array => new BsonArray(je.EnumerateArray().Select(e => ObjectToBsonValue(e))),
                    _ => new BsonString(je.GetRawText())
                };
            }

            return BsonValue.Create(value);
        }

        private static BsonDocument JsonElementToBsonDocument(JsonElement element)
        {
            var doc = new BsonDocument();
            foreach (var prop in element.EnumerateObject())
            {
                doc[prop.Name] = ObjectToBsonValue(prop.Value);
            }
            return doc;
        }

        public async Task<StepExecuteResponseDto> StepExecuteAsync(string tenantId, StepExecuteRequestDto dto)
        {
            var workflow = await _workflowRepository.GetWorkflowAsync(tenantId, dto.WorkflowId);
            if (workflow == null)
            {
                return new StepExecuteResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Workflow", "Workflow not found" } }
                };
            }
            var targetNode = workflow.Nodes.FirstOrDefault(n => n.Id == dto.NodeId);
            if (targetNode == null)
            {
                return new StepExecuteResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Node", "Node not found" } }
                };
            }

            var oldExecution = await _executionRepository.GetByIdAsync(dto.SourceExecutionId, tenantId);
            var ancestors = _workflowEngineService.GetTopologicalAncestorsAndTarget(workflow, dto.NodeId);
            var triggerNodes = ancestors.Where(n => n.Category == "trigger").ToList();
            var hasAnyPinnedTriggerData = triggerNodes.Any(n => n.PinData != null && n.PinData.Count > 0);


            var currentTriggerNodeId = "";

            if (String.IsNullOrWhiteSpace(dto.SourceExecutionId) && !hasAnyPinnedTriggerData)
            {
                workflow.TestMeta = new TestWorkflowMeta
                {
                    IsListening = true,
                    ListenerTriggerNodes = triggerNodes,
                    UserIds = BlocksContext.GetContext().UserId != null ? new List<string> { BlocksContext.GetContext().UserId } : new List<string>(),
                    CompletionNodeId = dto.NodeId

                };
                await _workflowRepository.UpdateWorkflowAsync(workflow);
                return new StepExecuteResponseDto
                {
                    IsSuccess = true,
                    Message = "Trigger nodes Listining",
                    Code = "101"
                };

            }

            if (hasAnyPinnedTriggerData)
            {
                currentTriggerNodeId = triggerNodes.FirstOrDefault(n => n.PinData != null && n.PinData.Count > 0)?.Id;
            }

            if (oldExecution != null && oldExecution.TriggerMetadata != null && !String.IsNullOrWhiteSpace(oldExecution.TriggerMetadata.TriggerNodeId))
            {
                currentTriggerNodeId = oldExecution.TriggerMetadata.TriggerNodeId;
            }

            if (!String.IsNullOrWhiteSpace(dto.TriggerNodeId))
            {
                if (triggerNodes.Any(n => n.Id == dto.TriggerNodeId) || oldExecution?.TriggerMetadata?.TriggerNodeId == dto.TriggerNodeId)
                {
                    currentTriggerNodeId = dto.TriggerNodeId;
                }
                else
                {
                    return new StepExecuteResponseDto
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string> { { "Message", "Trigger node not found in ancestors" } }
                    };
                }
            }

            var triggerNode = workflow.Nodes.FirstOrDefault(n => n.Id == currentTriggerNodeId);
            if (triggerNode == null)
            {
                return new StepExecuteResponseDto
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "Message", "Trigger node not found" } }
                };
            }

            TriggerMetadata triggerMetadata = new TriggerMetadata
            {
                TriggerNodeId = triggerNode.Id,
                TriggerType = triggerNode.Type,
                TriggerData = new BsonArray()
            };
            if (oldExecution != null && oldExecution.TriggerMetadata != null && !String.IsNullOrWhiteSpace(oldExecution.TriggerMetadata.TriggerNodeId))
            {
                triggerMetadata = oldExecution.TriggerMetadata;
            }
            else
            {
                triggerMetadata = new TriggerMetadata
                {
                    TriggerNodeId = triggerNode.Id,
                    TriggerType = triggerNode.Type,
                    TriggerData = new BsonArray()
                };
            }
            if (triggerNode.PinData != null && triggerNode.PinData.Count > 0)
            {
                triggerMetadata = new TriggerMetadata
                {
                    TriggerNodeId = triggerNode.Id,
                    TriggerType = triggerNode.Type,
                    TriggerData = new BsonArray(triggerNode.PinData)
                };
            }


            workflow.TestMeta = new TestWorkflowMeta
            {
                IsListening = true,
                ListenerTriggerNodes = triggerNodes,
                UserIds = BlocksContext.GetContext().UserId != null ? new List<string> { BlocksContext.GetContext().UserId } : new List<string>(),
                CompletionNodeId = dto.NodeId

            };
            var execution = await CreateExecutionAsync(workflow, triggerMetadata, WorkflowExecutionMode.Test);
            execution.Context["Input"] = triggerMetadata.TriggerData ?? new BsonArray();
            execution.Status = WorkflowExecutionStatus.Queued;
            execution.ActiveNodeIds.Add(triggerNode.Id);
            await NotifyWorkflowStartedAsync(execution);
            var result = await _workflowEngineService.ExecuteStepNodeAsync(tenantId, execution.Id, triggerNode.Id, dto.NodeId, dto.SourceExecutionId);
            return new StepExecuteResponseDto
            {
                IsSuccess = true,
                ItemId = result.Id,
            };

        }


    }
}
