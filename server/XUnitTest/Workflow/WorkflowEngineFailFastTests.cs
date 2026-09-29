using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using Utilities.Api.Controllers;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Events;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TriggerWebhookV1;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Engine-level fail-fast behaviour: a node that fails stops the workflow. Runs the real
    /// <see cref="WorkflowEngineService"/> (immediate / in-process mode, the one a waiting webhook uses)
    /// against an in-memory repository that mirrors the Mongo repository's atomic updates, including
    /// its "a Failed execution is terminal" guards.
    /// </summary>
    public class WorkflowEngineFailFastTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private readonly InMemoryExecutionRepository _repo = new();
        private readonly List<string> _ran = new();

        public WorkflowEngineFailFastTests() => TestBlocksContext.Set(Tenant);

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        // ----- Graph: webhook trigger -> A -> B (and A -> C) ---------------------------

        private sealed class ScriptedNode : INodeExecutor
        {
            private readonly Func<NodeExecutionContext, Task<NodeExecutionResult>> _run;
            private readonly List<string> _ran;

            public ScriptedNode(string type, List<string> ran, Func<NodeExecutionContext, Task<NodeExecutionResult>> run)
            {
                NodeType = type;
                _ran = ran;
                _run = run;
            }

            public string NodeType { get; }
            public string Version => "1.0";

            public Task<NodeExecutionResult> RunAsync(NodeExecutionContext context)
            {
                _ran.Add(NodeType);
                return _run(context);
            }
        }

        private static NodeExecutionResult PassThrough(NodeExecutionContext ctx) =>
            NodeExecutionResult.Successful(ctx.InputItems.Select(i => new NodeOutputItem
            {
                Data = new NodeOutputItemData { Input = i.Data.Output, Output = new BsonDocument("ok", true) },
                Branch = "source",
                ParentItemIds = new List<string> { i.Id },
            }).ToList());

        private static NodeEntity Node(string id, string type, string category = "action", BsonDocument? parameters = null) => new()
        {
            Id = id,
            Name = $"Node {id}",
            Category = category,
            Type = type,
            Version = "1.0",
            Position = new Position(),
            Parameters = parameters ?? new BsonDocument(),
        };

        private static EdgeEnity Edge(string source, string target) => new()
        {
            Id = $"{source}-{target}",
            Source = source,
            Target = target,
            SourceHandle = "source",
            TargetHandle = "target",
        };

        private static WorkflowEntity Graph(BsonDocument? triggerParameters = null, bool branchToC = false)
        {
            var workflow = new WorkflowEntity
            {
                ItemId = "wf-1",
                Name = "Orders",
                TenantId = Tenant,
                IsPublished = true,
                PublishedVersionId = "ver-1",
                Nodes = new List<NodeEntity>
                {
                    Node("trigger", "webhook", "trigger", triggerParameters),
                    Node("a", "nodeA"),
                    Node("b", "nodeB"),
                },
                Edges = new List<EdgeEnity> { Edge("trigger", "a"), Edge("a", "b") },
            };
            if (branchToC)
            {
                workflow.Nodes.Add(Node("c", "nodeC"));
                workflow.Edges.Add(Edge("a", "c"));
            }
            return workflow;
        }

        private WorkflowEngineService Engine(params INodeExecutor[] executors)
        {
            var all = new List<INodeExecutor> { new TriggerWebhookV1Node() };
            all.AddRange(executors);
            var notifications = new Mock<IWorkflowNotificationService>();
            notifications
                .Setup(n => n.NotifyExecutionEventAsync(It.IsAny<WorkflowExecutionEntity>(), It.IsAny<NodeExecutionEntity?>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            return new WorkflowEngineService(
                _repo,
                all,
                Mock.Of<IMessageClient>(),
                NullLogger<WorkflowEngineService>.Instance,
                notifications.Object,
                Mock.Of<IServiceProvider>());
        }

        private async Task<string> SeedExecutionAsync(WorkflowEntity workflow)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = Tenant,
                WorkflowId = workflow.ItemId,
                WorkflowName = workflow.Name,
                WorkflowSnapshot = workflow,
                Status = WorkflowExecutionStatus.Queued,
                ExecutionMode = WorkflowExecutionMode.Production,
                TriggerMetadata = new TriggerMetadata { TriggerNodeId = "trigger" },
                Context = new BsonDocument { { "Input", new BsonArray { new BsonDocument("orderId", "o-1") } } },
                ActiveNodeIds = new List<string> { "trigger" },
            };
            await _repo.CreateAsync(execution);
            return execution.Id;
        }

        private static AddExcuationNodeEvent Start(string executionId) => new()
        {
            TenantId = Tenant,
            WorkflowId = "wf-1",
            WorkflowExecutionId = executionId,
            NodeId = "trigger",
        };

        // ----- Tests --------------------------------------------------------------------

        [Fact]
        public async Task A_failing_node_stops_the_workflow_and_no_downstream_node_runs()
        {
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, ctx => Task.FromResult(
                    NodeExecutionResult.Failed("Upstream returned 503", new List<NodeOutputItem>()))),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());

            var execution = await engine.RunNodeInProcessAsync(Start(executionId));

            _ran.Should().Equal("nodeA");
            execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            execution.FailedNodeId.Should().Be("a");
            execution.FailedNodeName.Should().Be("Node a");
            execution.ErrorMessage.Should().Be("Upstream returned 503");
            execution.FinishedAt.Should().NotBeNull();
            execution.ActiveNodeIds.Should().BeEmpty();
            execution.NodeExecutions.Select(ne => ne.NodeId).Should().Equal("trigger", "a");
            var failed = execution.NodeExecutions.Single(ne => ne.NodeId == "a");
            failed.Status.Should().Be(NodeExecutionStatus.Failed);
            failed.Error.Should().Be("Upstream returned 503");
        }

        [Fact]
        public async Task The_execution_stays_Failed_when_its_last_active_node_drains()
        {
            // The failed node is the only active node; removing it from ActiveNodeIds must not let the
            // "no active nodes left" rule flip the execution to Completed.
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, _ => Task.FromResult(NodeExecutionResult.Failed("boom"))),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());

            await engine.RunNodeInProcessAsync(Start(executionId));

            (await _repo.GetByIdAsync(executionId, Tenant))!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        }

        [Fact]
        public async Task A_failing_node_keeps_its_error_items_on_the_execution_record()
        {
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, ctx =>
                {
                    var outputs = new List<NodeOutputItem>
                    {
                        new()
                        {
                            Data = new NodeOutputItemData
                            {
                                Output = new BsonDocument { { "error", true }, { "message", "bad item" } },
                            },
                            Branch = "source",
                            ParentItemIds = new List<string> { ctx.InputItems[0].Id },
                        },
                    };
                    return Task.FromResult(NodeExecutionResult.Failed("bad item", outputs));
                }),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());

            var execution = await engine.RunNodeInProcessAsync(Start(executionId));

            var failed = execution!.NodeExecutions.Single(ne => ne.NodeId == "a");
            failed.OutputItemCount.Should().Be(1);
            var persisted = await _repo.GetAllItemsByNodeExecutionIdAsync(failed.Id, Tenant);
            persisted.Should().ContainSingle();
            persisted[0].Data.Output["error"].AsBoolean.Should().BeTrue();
            _ran.Should().NotContain("nodeB");
        }

        [Fact]
        public async Task A_sibling_branch_does_not_run_after_another_branch_failed()
        {
            // trigger -> a -> { b, c }; b fails, so c (dispatched after it in immediate mode) is skipped.
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, ctx => Task.FromResult(PassThrough(ctx))),
                new ScriptedNode("nodeB", _ran, _ => Task.FromResult(NodeExecutionResult.Failed("b broke"))),
                new ScriptedNode("nodeC", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph(branchToC: true));

            var execution = await engine.RunNodeInProcessAsync(Start(executionId));

            _ran.Should().Equal("nodeA", "nodeB");
            execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            execution.FailedNodeId.Should().Be("b");
            execution.NodeExecutions.Should().NotContain(ne => ne.NodeId == "c");
        }

        [Fact]
        public async Task A_node_that_throws_fails_the_execution_without_leaking_the_exception()
        {
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, _ => throw new InvalidOperationException("mongodb://secret@host failed")),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());

            var execution = await engine.RunNodeInProcessAsync(Start(executionId));

            _ran.Should().Equal("nodeA");
            execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            execution.FailedNodeId.Should().Be("a");
            // Execution-level message (what a webhook caller can see) is generic...
            execution.ErrorMessage.Should().Be("Node 'Node a' failed unexpectedly.");
            // ...and no stack trace is stored anywhere on the record.
            var failed = execution.NodeExecutions.Single(ne => ne.NodeId == "a");
            failed.Status.Should().Be(NodeExecutionStatus.Failed);
            failed.Error.Should().NotContain(" at ");
            failed.Error.Should().NotContain("System.InvalidOperationException");
        }

        [Fact]
        public async Task A_downstream_failure_is_not_charged_back_to_the_completed_parent()
        {
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, ctx => Task.FromResult(PassThrough(ctx))),
                new ScriptedNode("nodeB", _ran, _ => Task.FromResult(NodeExecutionResult.Failed("b broke"))));
            var executionId = await SeedExecutionAsync(Graph());

            var execution = await engine.RunNodeInProcessAsync(Start(executionId));

            execution!.NodeExecutions.Single(ne => ne.NodeId == "a").Status.Should().Be(NodeExecutionStatus.Completed);
            execution.NodeExecutions.Single(ne => ne.NodeId == "b").Status.Should().Be(NodeExecutionStatus.Failed);
            execution.FailedNodeId.Should().Be("b");
        }

        [Fact]
        public async Task Cancellation_propagates_and_the_node_is_not_left_running()
        {
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, _ => throw new OperationCanceledException()),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());

            var act = () => engine.RunNodeInProcessAsync(Start(executionId));

            await act.Should().ThrowAsync<OperationCanceledException>();
            _ran.Should().Equal("nodeA");
            var execution = await _repo.GetByIdAsync(executionId, Tenant);
            execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
            execution.NodeExecutions.Single(ne => ne.NodeId == "a").Status.Should().Be(NodeExecutionStatus.Failed);
            execution.ErrorMessage.Should().Be("The execution was cancelled.");
        }

        [Fact]
        public async Task A_node_dispatched_after_the_execution_failed_is_skipped()
        {
            // Queue mode: a message for node b can still arrive after a sibling failed the execution.
            var engine = Engine(new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());
            var execution = await _repo.GetByIdAsync(executionId, Tenant);
            execution!.Status = WorkflowExecutionStatus.Failed;
            await _repo.UpdateAsync(execution);

            await engine.RunNodeAsync(new AddExcuationNodeEvent
            {
                TenantId = Tenant, WorkflowId = "wf-1", WorkflowExecutionId = executionId, NodeId = "b",
            });

            _ran.Should().BeEmpty();
        }

        [Fact]
        public async Task A_successful_run_still_completes()
        {
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, ctx => Task.FromResult(PassThrough(ctx))),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));
            var executionId = await SeedExecutionAsync(Graph());

            var execution = await engine.RunNodeInProcessAsync(Start(executionId));

            _ran.Should().Equal("nodeA", "nodeB");
            execution!.Status.Should().Be(WorkflowExecutionStatus.Completed);
            execution.FailedNodeId.Should().BeNull();
        }

        // ----- Waiting webhook: the caller gets an error response ------------------------

        private WorkflowExecutionService WebhookService(WorkflowEntity workflow, WorkflowEngineService engine)
        {
            var workflows = new Mock<IWorkflowRepository>();
            workflows.Setup(r => r.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);
            var versions = new Mock<IWorkflowVersionRepository>();
            versions.Setup(r => r.GetWorkflowVersionAsync(Tenant, "ver-1")).ReturnsAsync(new WorkflowVersionEntity
            {
                ItemId = "ver-1", WorkflowId = "wf-1", TenantId = Tenant, Name = "v1", Snapshot = workflow,
            });
            var notifications = new Mock<IWorkflowNotificationService>();
            notifications
                .Setup(n => n.NotifyExecutionEventAsync(It.IsAny<WorkflowExecutionEntity>(), It.IsAny<NodeExecutionEntity?>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            return new WorkflowExecutionService(
                workflows.Object,
                _repo,
                Mock.Of<IMessageClient>(),
                NullLogger<WorkflowExecutionService>.Instance,
                engine,
                versions.Object,
                notifications.Object,
                Mock.Of<IWorkflowAuthService>(),
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IDelegationGrantFactory>());
        }

        private static JsonElement Body()
        {
            using var doc = JsonDocument.Parse("{\"orderId\":\"o-1\"}");
            return doc.RootElement.Clone();
        }

        [Theory]
        [InlineData("first")]
        [InlineData("all")]
        [InlineData("none")]
        public async Task A_waiting_webhook_caller_gets_a_failure_naming_the_node(string responseData)
        {
            var workflow = Graph(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", responseData } });
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, _ => Task.FromResult(NodeExecutionResult.Failed("Invalid JSON body: expected '}'"))),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));

            var response = await WebhookService(workflow, engine).TriggerWebhookAsync("wf-1", "trigger", Tenant, Body());

            response.Should().NotBeNull();
            response.Status.Should().Be(WorkflowWebhookResponseDto.FailedStatus);
            response.ExecutionId.Should().NotBeNullOrWhiteSpace();
            response.Data.Should().BeNull();
            response.Error.Should().NotBeNull();
            response.Error!.NodeId.Should().Be("a");
            response.Error.NodeName.Should().Be("Node a");
            response.Error.Message.Should().Be("Invalid JSON body: expected '}'");
            _ran.Should().Equal("nodeA");
            (await _repo.GetByIdAsync(response.ExecutionId, Tenant))!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        }

        [Fact]
        public async Task A_waiting_webhook_never_echoes_an_unexpected_exception()
        {
            var workflow = Graph(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", "first" } });
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, _ => throw new InvalidOperationException("connection string=secret")),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));

            var response = await WebhookService(workflow, engine).TriggerWebhookAsync("wf-1", "trigger", Tenant, Body());

            response.Status.Should().Be(WorkflowWebhookResponseDto.FailedStatus);
            response.Error!.Message.Should().Be("Node 'Node a' failed unexpectedly.");
            JsonSerializer.Serialize(response).Should().NotContain("secret");
        }

        [Fact]
        public async Task A_waiting_webhook_that_succeeds_still_returns_the_last_node_output()
        {
            var workflow = Graph(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", "first" } });
            var engine = Engine(
                new ScriptedNode("nodeA", _ran, ctx => Task.FromResult(PassThrough(ctx))),
                new ScriptedNode("nodeB", _ran, ctx => Task.FromResult(PassThrough(ctx))));

            var response = await WebhookService(workflow, engine).TriggerWebhookAsync("wf-1", "trigger", Tenant, Body());

            response.Status.Should().Be("Completed");
            response.Error.Should().BeNull();
            response.Data!.Value.GetProperty("ok").GetBoolean().Should().BeTrue();
        }

        [Fact]
        public void The_failed_webhook_message_is_bounded()
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "e", TenantId = Tenant, WorkflowId = "wf-1", WorkflowName = "Orders",
                WorkflowSnapshot = Graph(), TriggerMetadata = new TriggerMetadata(),
                Status = WorkflowExecutionStatus.Failed, FailedNodeId = "a", FailedNodeName = "Node a",
                ErrorMessage = new string('x', 5000),
            };

            var response = WorkflowExecutionService.BuildFailedWebhookResponse("e", execution);

            response.Error!.Message.Length.Should().BeLessThanOrEqualTo(WorkflowExecutionService.MaxWebhookErrorMessageLength + 1);
        }

        [Fact]
        public void A_failed_webhook_with_no_record_still_answers_with_a_failure()
        {
            var response = WorkflowExecutionService.BuildFailedWebhookResponse("e", execution: null);

            response.Status.Should().Be(WorkflowWebhookResponseDto.FailedStatus);
            response.Error!.Message.Should().Be("The workflow execution failed.");
        }

        [Fact]
        public async Task The_webhook_endpoint_answers_a_failed_execution_with_500_and_the_error_body()
        {
            var failed = new WorkflowWebhookResponseDto
            {
                ExecutionId = "exec-9",
                Status = WorkflowWebhookResponseDto.FailedStatus,
                Error = new WorkflowWebhookErrorDto { NodeId = "a", NodeName = "Node a", Message = "Upstream returned 503" },
            };
            var executions = new Mock<IWorkflowExecutionService>();
            executions.Setup(s => s.TriggerWebhookAsync("wf-1", "trigger", Tenant, It.IsAny<JsonElement>())).ReturnsAsync(failed);
            executions.Setup(s => s.TriggerTestWebhookAsync("wf-1", "trigger", Tenant, It.IsAny<JsonElement>())).ReturnsAsync(failed);
            var controller = new WorkflowController(
                Mock.Of<IWorkflowService>(), Mock.Of<IWorkflowVersionService>(), executions.Object, Mock.Of<IWorkflowImportService>());

            foreach (var result in new[]
            {
                await controller.Webhook(Tenant, "wf-1", "trigger", Body()),
                await controller.TestWebhook(Tenant, "wf-1", "trigger", Body()),
            })
            {
                var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
                objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
                objectResult.Value.Should().BeSameAs(failed);
            }

            var json = JsonSerializer.Serialize(failed, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var parsed = JsonDocument.Parse(json);
            parsed.RootElement.GetProperty("executionId").GetString().Should().Be("exec-9");
            parsed.RootElement.GetProperty("status").GetString().Should().Be("Failed");
            parsed.RootElement.GetProperty("error").GetProperty("nodeId").GetString().Should().Be("a");
            parsed.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Be("Upstream returned 503");
        }

        [Fact]
        public async Task A_queued_webhook_keeps_answering_200()
        {
            var executions = new Mock<IWorkflowExecutionService>();
            executions.Setup(s => s.TriggerWebhookAsync("wf-1", "trigger", Tenant, It.IsAny<JsonElement>()))
                .ReturnsAsync(new WorkflowWebhookResponseDto { ExecutionId = "exec-1", Status = "Queued" });
            var controller = new WorkflowController(
                Mock.Of<IWorkflowService>(), Mock.Of<IWorkflowVersionService>(), executions.Object, Mock.Of<IWorkflowImportService>());

            var result = await controller.Webhook(Tenant, "wf-1", "trigger", Body());

            result.Should().BeOfType<OkObjectResult>();
        }

        // ----- In-memory repository mirroring WorkflowExecutionRepository ------------------

        /// <summary>
        /// Stores copies (BSON round-trip) so the engine's in-memory mutations never leak into "the
        /// database", exactly as with Mongo. Atomic operations mirror the Mongo filters/updates.
        /// </summary>
        private sealed class InMemoryExecutionRepository : IWorkflowExecutionRepository
        {
            private readonly Dictionary<string, BsonDocument> _executions = new();
            private readonly List<WorkflowItemExecutionEntity> _items = new();

            private static BsonDocument Store(WorkflowExecutionEntity e) => e.ToBsonDocument();
            private static WorkflowExecutionEntity Load(BsonDocument d) => BsonSerializer.Deserialize<WorkflowExecutionEntity>(d);

            private void Mutate(string id, Func<WorkflowExecutionEntity, bool> change)
            {
                if (!_executions.TryGetValue(id, out var doc)) return;
                var e = Load(doc);
                if (change(e)) _executions[id] = Store(e);
            }

            public Task<WorkflowExecutionEntity> CreateAsync(WorkflowExecutionEntity execution)
            {
                _executions[execution.Id] = Store(execution);
                return Task.FromResult(execution);
            }

            public Task<WorkflowExecutionEntity?> GetByIdAsync(string id, string tenantId) =>
                Task.FromResult(_executions.TryGetValue(id, out var d) ? Load(d) : null);

            public Task UpdateAsync(WorkflowExecutionEntity execution)
            {
                _executions[execution.Id] = Store(execution);
                return Task.CompletedTask;
            }

            public Task<bool> AtomicCompleteNodeAsync(string executionId, string tenantId, string completedNodeId, List<string> nextNodeIds)
            {
                var completed = false;
                Mutate(executionId, e =>
                {
                    e.ActiveNodeIds.RemoveAll(n => n == completedNodeId);
                    foreach (var n in nextNodeIds.Where(n => !e.ActiveNodeIds.Contains(n))) e.ActiveNodeIds.Add(n);
                    if (e.ActiveNodeIds.Count == 0 && e.Status != WorkflowExecutionStatus.Failed)
                    {
                        e.Status = WorkflowExecutionStatus.Completed;
                        e.FinishedAt = DateTime.UtcNow;
                        completed = true;
                    }
                    return true;
                });
                return Task.FromResult(completed);
            }

            public Task AtomicFinalizeExecutionAsync(string executionId, string tenantId)
            {
                Mutate(executionId, e =>
                {
                    if (e.Status == WorkflowExecutionStatus.Failed) return false;
                    e.ActiveNodeIds.Clear();
                    e.Status = WorkflowExecutionStatus.Completed;
                    e.FinishedAt = DateTime.UtcNow;
                    return true;
                });
                return Task.CompletedTask;
            }

            public Task<bool> AtomicAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution)
            {
                var added = false;
                Mutate(executionId, e =>
                {
                    if (e.Status == WorkflowExecutionStatus.Failed) return false;
                    e.NodeExecutions.Add(nodeExecution);
                    e.Status = WorkflowExecutionStatus.Running;
                    added = true;
                    return true;
                });
                return Task.FromResult(added);
            }

            public Task AtomicUpdateNodeExecutionCompletedAsync(string executionId, string tenantId, string nodeExecutionId, int outputItemCount, Dictionary<string, int> outputCountsByBranch, BsonDocument? contextUpdates)
            {
                Mutate(executionId, e =>
                {
                    var ne = e.NodeExecutions.FirstOrDefault(n => n.Id == nodeExecutionId);
                    if (ne == null) return false;
                    ne.Status = NodeExecutionStatus.Completed;
                    ne.OutputItemCount = outputItemCount;
                    ne.OutputCountsByBranch = outputCountsByBranch;
                    ne.EndedAt = DateTime.UtcNow;
                    if (contextUpdates != null) foreach (var el in contextUpdates) e.Context[el.Name] = el.Value;
                    return true;
                });
                return Task.CompletedTask;
            }

            public Task AtomicUpdateNodeExecutionFailedAsync(string executionId, string tenantId, string nodeExecutionId, string nodeError, int outputItemCount, Dictionary<string, int> outputCountsByBranch, string failedNodeId, string failedNodeName, string executionErrorMessage)
            {
                Mutate(executionId, e =>
                {
                    var ne = e.NodeExecutions.FirstOrDefault(n => n.Id == nodeExecutionId);
                    if (ne == null) return false;
                    ne.Status = NodeExecutionStatus.Failed;
                    ne.EndedAt = DateTime.UtcNow;
                    ne.Error = nodeError;
                    ne.OutputItemCount = outputItemCount;
                    ne.OutputCountsByBranch = outputCountsByBranch;
                    e.Status = WorkflowExecutionStatus.Failed;
                    e.ErrorMessage = executionErrorMessage;
                    e.FailedNodeId = failedNodeId;
                    e.FailedNodeName = failedNodeName;
                    e.FinishedAt = DateTime.UtcNow;
                    return true;
                });
                return Task.CompletedTask;
            }

            public Task<List<WorkflowExecutionEntity>> GetByWorkflowIdAsync(string workflowId, string tenantId) =>
                Task.FromResult(_executions.Values.Select(Load).Where(e => e.WorkflowId == workflowId).ToList());

            public Task AddItemsAsync(string tenantId, List<WorkflowItemExecutionEntity> items)
            {
                _items.AddRange(items);
                return Task.CompletedTask;
            }

            public Task<List<WorkflowItemExecutionEntity>> GetItemsByNodeIdsAsync(string workflowExecutionId, List<Dictionary<string, string>> nodeIdBranchPairs, string tenantId) =>
                Task.FromResult(_items.Where(i => i.WorkflowExecutionId == workflowExecutionId
                    && nodeIdBranchPairs.Any(p => p["NodeId"] == i.NodeId && p["Branch"] == i.Branch)).ToList());

            public Task<List<WorkflowItemExecutionEntity>> GetAllItemsByExecutionIdAsync(string workflowExecutionId, string tenantId) =>
                Task.FromResult(_items.Where(i => i.WorkflowExecutionId == workflowExecutionId).ToList());

            public Task<List<WorkflowItemExecutionEntity>> GetAllItemsByNodeExecutionIdAsync(string nodeExecutionId, string tenantId) =>
                Task.FromResult(_items.Where(i => i.NodeExecutionId == nodeExecutionId).OrderBy(i => i.ItemIndex).ToList());

            public Task<WorkflowExecutionEntity> GetLastCompletedExecution(string tenantId, string workflowId) =>
                Task.FromResult(_executions.Values.Select(Load)
                    .Where(e => e.WorkflowId == workflowId && e.Status == WorkflowExecutionStatus.Completed)
                    .OrderByDescending(e => e.FinishedAt).First());
        }
    }
}
