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
using PdfSharpCore.Pdf.Content.Objects;



namespace DomainService.Workflow.Services
{
    [ExcludeFromCodeCoverage]
    public class WorkflowExecutionService : IWorkflowExecutionService
    {
        private readonly IWorkflowRepository _workflowRepository;
        private readonly IWorkflowExecutionRepository _executionRepository;

        private readonly IMessageClient _messageClient;

        private readonly ILogger<WorkflowExecutionService> _logger;
        private readonly IWorkflowEngineService _workflowEngineService;

        public WorkflowExecutionService(
            IWorkflowRepository workflowRepository,
            IWorkflowExecutionRepository executionRepository,
            IMessageClient messageClient,
            ILogger<WorkflowExecutionService> logger,
            IWorkflowEngineService workflowEngineService
            )
        {
            _workflowRepository = workflowRepository;
            _executionRepository = executionRepository;
            _messageClient = messageClient;
            _workflowEngineService = workflowEngineService;
            _logger = logger;
        }


        public async Task<WorkflowExecutionModel> CreateExecutionAsync(string workflowId, string triggerId, string tenantId)
        {

            var workflow = await _workflowRepository.GetWorkflowAsync(workflowId, tenantId)
                ?? throw new InvalidOperationException($"Workflow {workflowId} not found");

            var execution = new WorkflowExecutionModel
            {
                Id = Guid.NewGuid().ToString().Replace("-", ""),
                WorkflowId = workflowId,
                WorkflowName = workflow.Name,
                TenantId = workflow.TenantId,
                WorkflowSnapshot = workflow,
                Status = WorkflowExecutionStatus.Init,
                NodeExecutions = new List<NodeExecutionModel>(),
                StartedAt = DateTime.UtcNow,
            };
            return await _executionRepository.CreateAsync(execution);
        }


        public async Task<WorkflowWebhookResponseDto?> WebhookStartAsync(string workflowId, string triggerId, string tenantId, JsonElement input)
        {
            WorkflowExecutionModel execution;
            try
            {
                var workflow = await _workflowRepository.GetWorkflowAsync(workflowId, tenantId)
                ?? throw new InvalidOperationException($"Workflow {workflowId} not found");
                var triggerNode = workflow.Nodes.FirstOrDefault(n => n.Id == triggerId);
                if (triggerNode == null)
                {
                    throw new InvalidOperationException($"Trigger node {triggerId} not found in workflow {workflowId}");
                }
                var authType = triggerNode.Parameters.GetValue("authType");
                if (authType != null && authType.ToString().ToLower() == "blocksAccessToken".ToLower())
                {
                    var blocksContext = BlocksContext.GetContext();
                    if (!blocksContext.IsAuthenticated) throw new UnauthorizedAccessException();

                }
                execution = await CreateExecutionAsync(workflowId, triggerId, tenantId);

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


                execution.Context["Input"] = normalizedInput;
                execution.Status = WorkflowExecutionStatus.Queued;
                execution.ActiveNodeIds.Add(triggerId);

                await _executionRepository.UpdateAsync(execution);

                var payload = new AddExcuationNodeEvent
                {
                    WorkflowId = workflowId,
                    WorkflowExecutionId = execution.Id,
                    NodeId = triggerId,
                    ProjectKey = tenantId
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
                    var lastNodeOutput = await _executionRepository.GetAllItemsByNodeExecutionIdAsync(lastExecutationNode.Id, tenantId);
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
            var projectKey = emailEvent.ProjectKey;
            if (string.IsNullOrEmpty(projectKey))
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

            var workflows = await _workflowRepository.GetWorkflowsByMailServerConfigurationIdAsync(emailEvent.Mail.MailServerConfigurationId, projectKey);
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

                    var execution = await CreateExecutionAsync(workflow.ItemId, triggerNode.Id, projectKey);
                    var emailJson = JsonSerializer.Serialize(emailEvent.Mail);
                    var emailBsonDoc = BsonDocument.Parse(emailJson);
                    execution.Context["Input"] = new BsonArray { emailBsonDoc };

                    execution.Status = WorkflowExecutionStatus.Queued;
                    execution.ActiveNodeIds.Add(triggerNode.Id);

                    await _executionRepository.UpdateAsync(execution);
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
                    _logger.LogInformation("Queued execution {ExecutionId} for WorkflowId: {WorkflowId}", execution.Id, workflow.ItemId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create execution for WorkflowId: {WorkflowId}", workflow.ItemId);
                }
            }
        }

        public async Task<WorkflowExecutionsGetResponseDto> GetExecutionsByWorkflowIdAsync(WorkflowExecutionsGetRequestDto dto)
        {
            var executions = await _executionRepository.GetByWorkflowIdAsync(dto.WorkflowId, dto.ProjectKey);

            var executionItems = executions.Select(e => new WorkflowExecutionItemDto
            {
                Id = e.Id!,
                WorkflowId = e.WorkflowId,
                WorkflowName = e.WorkflowName,
                Status = e.Status,
                StartedAt = e.StartedAt,
                FinishedAt = e.FinishedAt,
                ErrorMessage = e.ErrorMessage,
                TriggerType = e.TriggerType,
                AttemptNumber = e.AttemptNumber
            }).ToList();

            return new WorkflowExecutionsGetResponseDto
            {
                TotalCount = executionItems.Count,
                Data = executionItems,
                Errors = null
            };
        }

        public async Task<WorkflowExecutionGetResponseDto> GetExecutionByIdAsync(WorkflowExecutionGetRequestDto dto)
        {
            var execution = await _executionRepository.GetByIdAsync(dto.ExecutionId, dto.ProjectKey)
                ?? throw new InvalidOperationException($"Execution {dto.ExecutionId} not found");

            // Get all workflow items - frontend will organize them into input/output structure
            var allItems = await _executionRepository.GetAllItemsByExecutionIdAsync(dto.ExecutionId, dto.ProjectKey);

            var nodes = execution.WorkflowSnapshot.Nodes.Select(item => new NodeDto
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.Type,
                Version = item.Version,
                Category = item.Category,
                Position = item.Position,
                Parameters = BsonJsonConverter.ToJsonElement(item.Parameters),
                Settings = BsonJsonConverter.ToJsonElement(item.Settings)
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
                Id = execution.Id,
                WorkflowId = execution.WorkflowId,
                WorkflowName = execution.WorkflowName,
                Status = execution.Status,
                StartedAt = execution.StartedAt,
                FinishedAt = execution.FinishedAt,
                ErrorMessage = execution.ErrorMessage,
                TriggerType = execution.TriggerType,
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
            };
        }

        public async Task DataTriggerStartAsync(DataChangeEvent dataEvent)
        {
            _logger.LogInformation("Starting DataTriggerStartAsync for Collection: {CollectionName}, Operation: {Operation}",
                dataEvent.CollectionName, dataEvent.Operation);

            var projectKey = dataEvent.ProjectKey;
            if (string.IsNullOrEmpty(projectKey))
            {
                _logger.LogError("ProjectKey (TenantId) is null or empty in BlocksContext");
                return;
            }

            var operationStr = dataEvent.Operation.ToString();
            var workflows = await _workflowRepository.GetWorkflowsByDataCollectionAsync(
                dataEvent.CollectionName, operationStr, projectKey);

            _logger.LogInformation("Found {Count} workflows for Collection: {CollectionName}, Operation: {Operation}",
                workflows.Count, dataEvent.CollectionName, operationStr);

            var triggerData = BuildTriggerData(dataEvent, operationStr);

            foreach (var workflow in workflows)
            {
                await QueueDataTriggerExecutionAsync(workflow, dataEvent, operationStr, triggerData, projectKey);
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
            WorkflowModel workflow, DataChangeEvent dataEvent, string operationStr,
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

                var execution = await CreateExecutionAsync(workflow.ItemId, triggerNode.Id, projectKey);
                execution.Context["Input"] = triggerData;
                execution.Status = WorkflowExecutionStatus.Queued;
                execution.ActiveNodeIds.Add(triggerNode.Id);

                await _executionRepository.UpdateAsync(execution);
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

        #region Helpers

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

        #endregion
    }
}
