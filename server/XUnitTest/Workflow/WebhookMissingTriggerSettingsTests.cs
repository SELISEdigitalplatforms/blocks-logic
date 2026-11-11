using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Blocks.Genesis;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Rollbar C18: <c>BsonDocument.GetValue</c> throws on a missing key, so a webhook trigger saved without
    /// <c>authType</c>, <c>httpResponseMode</c> or <c>httpResponseData</c> answered 500 — and the
    /// <c>httpResponseMode</c> read ran after the execution was saved as Queued. All three are now read
    /// before an execution exists: a missing <c>authType</c> is refused (401), the response settings fall
    /// back to <c>immediate</c> / <c>all</c>.
    /// </summary>
    public class WebhookMissingTriggerSettingsTests : IDisposable
    {
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IWorkflowEngineService> _engine = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly Mock<IInProcessGrantReleaser> _releaser = new();
        private readonly Mock<IDelegationGrantFactory> _grants = new();
        private readonly Mock<ILogger<WorkflowExecutionService>> _logger = new();
        private readonly Mock<ILogger<WorkflowExecutionLogger>> _executionLog = new();
        private readonly WorkflowExecutionService _sut;

        public WebhookMissingTriggerSettingsTests()
        {
            TestBlocksContext.Set("tenant-1");
            DelegatedTokenContext.Clear();
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
            _executions.Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => { e.Id = "exec-1"; return e; });
            _executionLog.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            _sut = new WorkflowExecutionService(
                _workflows.Object,
                _executions.Object,
                _messages.Object,
                _logger.Object,
                _engine.Object,
                _versions.Object,
                Mock.Of<IWorkflowNotificationService>(),
                _auth.Object,
                accessor.Object,
                _grants.Object,
                new WorkflowExecutionLogger(_executionLog.Object),
                _releaser.Object);
        }

        public void Dispose()
        {
            DelegatedTokenContext.Clear();
            TestBlocksContext.Clear();
        }

        // ---- fixtures ----

        private static NodeEntity Trigger(BsonDocument? parameters) => new()
        {
            Id = "hook",
            Name = "Webhook",
            Category = "trigger",
            Type = "webhook",
            Version = "v1",
            Position = new Position(),
            Parameters = parameters!,
        };

        private static NodeEntity Code() => new()
        {
            Id = "code",
            Name = "Code",
            Category = "action",
            Type = "code",
            Version = "v1",
            Position = new Position(),
            Parameters = new BsonDocument(),
        };

        private static WorkflowEntity Graph(BsonDocument? parameters) => new()
        {
            TenantId = "tenant-1",
            ItemId = "wf-1",
            Nodes = [Trigger(parameters), Code()],
            Edges = [new EdgeEnity { Id = "e1", Source = "hook", Target = "code", SourceHandle = "source", TargetHandle = "target" }],
        };

        private void Published(BsonDocument? parameters)
        {
            _workflows.Setup(r => r.GetWorkflowAsync("tenant-1", "wf-1")).ReturnsAsync(new WorkflowEntity
            {
                TenantId = "tenant-1", ItemId = "wf-1", IsPublished = true, PublishedVersionId = "ver-1",
            });
            _versions.Setup(r => r.GetWorkflowVersionAsync("tenant-1", "ver-1")).ReturnsAsync(new WorkflowVersionEntity
            {
                TenantId = "tenant-1", ItemId = "ver-1", WorkflowId = "wf-1", Name = "v1", Snapshot = Graph(parameters),
            });
        }

        private void Draft(BsonDocument? parameters, bool listening = true)
        {
            var draft = Graph(parameters);
            draft.TestMeta = new TestWorkflowMeta
            {
                IsListening = listening,
                ListenerTriggerNodes = listening ? [draft.Nodes[0]] : [],
            };
            _workflows.Setup(r => r.GetWorkflowAsync("tenant-1", "wf-1")).ReturnsAsync(draft);
        }

        /// <summary>The in-process run finishes; <paramref name="ran"/> lists the node executions it produced.</summary>
        private void InProcessRun(WorkflowExecutionStatus status, params NodeExecutionEntity[] ran) =>
            _engine.Setup(e => e.RunNodeInProcessAsync(It.IsAny<AddExcuationNodeEvent>()))
                .ReturnsAsync(RunResult(status, null, ran));

        private static WorkflowExecutionEntity RunResult(WorkflowExecutionStatus status, string? error, NodeExecutionEntity[] ran) => new()
        {
            Id = "exec-1",
            TenantId = "tenant-1",
            WorkflowId = "wf-1",
            WorkflowName = "wf",
            Status = status,
            ErrorMessage = error,
            TriggerMetadata = new TriggerMetadata { TriggerNodeId = "hook", TriggerType = "webhook" },
            WorkflowSnapshot = Graph(new BsonDocument()),
            NodeExecutions = ran.ToList(),
        };

        private void Outputs(string nodeExecutionId, params BsonDocument[] items) =>
            _executions.Setup(r => r.GetAllItemsByNodeExecutionIdAsync(nodeExecutionId, "tenant-1"))
                .ReturnsAsync(items.Select((o, i) => new WorkflowItemExecutionEntity
                {
                    Id = $"{nodeExecutionId}-item-{i}",
                    WorkflowExecutionId = "exec-1",
                    TenantId = "tenant-1",
                    NodeId = nodeExecutionId,
                    NodeExecutionId = nodeExecutionId,
                    NodeName = nodeExecutionId,
                    Branch = "source",
                    ParentItemIds = new List<string>(),
                    AncestorMap = new Dictionary<string, string>(),
                    Data = new NodeOutputItemData { Output = o },
                }).ToList());

        private static NodeExecutionEntity Ran(string nodeId, int runIndex) => new()
        {
            Id = nodeId + "-run",
            NodeId = nodeId,
            NodeName = nodeId,
            NodeType = "test",
            NodeVersion = "v1",
            RunIndex = runIndex,
            Status = NodeExecutionStatus.Completed,
            InputItemCount = 1,
            OutputItemCount = 1,
            OutputCountsByBranch = new Dictionary<string, int> { ["source"] = 1 },
        };

        private static JsonElement Json(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }

        private Task<WorkflowWebhookResponseDto?> Call(string body = "{}") =>
            _sut.TriggerWebhookAsync("wf-1", "hook", "tenant-1", Json(body));

        private Task<WorkflowWebhookResponseDto?> CallTest(string body = "{}") =>
            _sut.TriggerTestWebhookAsync("wf-1", "hook", "tenant-1", Json(body));

        private void VerifyQueuedOnce()
        {
            _executions.Verify(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()), Times.Once);
            _executions.Verify(r => r.UpdateAsync(It.Is<WorkflowExecutionEntity>(e => e.Status == WorkflowExecutionStatus.Queued)), Times.Once);
            _messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()), Times.Once);
        }

        private void VerifyNothingCreated()
        {
            _executions.Verify(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()), Times.Never);
            _executions.Verify(r => r.UpdateAsync(It.IsAny<WorkflowExecutionEntity>()), Times.Never);
            _messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()), Times.Never);
        }

        private void VerifyNoAuthCalls()
        {
            _auth.Verify(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), It.IsAny<string>()), Times.Never);
            _auth.Verify(a => a.IsAuthorized(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<WorkflowAuthService.AuthorizationConfig>()), Times.Never);
        }

        private void VerifyWarnings(Times times) =>
            _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

        // ---- happy path ----

        [Fact] // H1, H7, H10, C1, C8a
        public async Task Missing_response_mode_queues_the_execution_once()
        {
            Published(new BsonDocument { { "authType", "none" } });

            var result = await Call();

            result!.Status.Should().Be("Queued");
            result.ExecutionId.Should().Be("exec-1");
            VerifyQueuedOnce();
            _executionLog.Verify(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(ExecutionLogStages.ExecutionQueued)),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }

        [Fact] // H2
        public async Task Test_webhook_with_missing_response_mode_queues_a_test_execution()
        {
            Draft(new BsonDocument { { "authType", "none" } });

            var result = await CallTest();

            result!.Status.Should().Be("Queued");
            _executions.Verify(r => r.CreateAsync(It.Is<WorkflowExecutionEntity>(e => e.ExecutionMode == WorkflowExecutionMode.Test)), Times.Once);
            VerifyQueuedOnce();
        }

        [Fact] // H4
        public async Task Auth_none_with_both_response_settings_missing_makes_no_auth_calls()
        {
            Published(new BsonDocument { { "authType", "none" } });

            (await Call())!.Status.Should().Be("Queued");

            VerifyNoAuthCalls();
            VerifyQueuedOnce();
        }

        [Theory] // H5, H9
        [InlineData("last")]
        [InlineData("LAST")]
        [InlineData("Last")]
        public async Task Last_mode_with_missing_response_data_returns_every_item_of_the_last_node(string mode)
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", mode } });
            InProcessRun(WorkflowExecutionStatus.Completed, Ran("hook", 1), Ran("code", 2));
            Outputs("code-run", new BsonDocument("a", 1), new BsonDocument("a", 2));

            var result = await Call();

            result!.Status.Should().Be("Completed");
            result.Data!.Value.GetRawText().Replace(" ", "").Should().Be("[{\"a\":1},{\"a\":2}]");
            _messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()), Times.Never);
        }

        [Fact] // H6
        public async Task Last_mode_with_missing_response_data_returns_the_trigger_output_when_nothing_ran_after_it()
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" } });
            InProcessRun(WorkflowExecutionStatus.Completed, Ran("hook", 1));
            Outputs("hook-run", new BsonDocument());

            var result = await Call();

            result!.Data!.Value.GetRawText().Replace(" ", "").Should().Be("[{}]");
        }

        [Fact] // H6
        public async Task Last_mode_with_missing_response_data_returns_an_empty_array_when_no_node_is_selected()
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" } });
            InProcessRun(WorkflowExecutionStatus.Completed);

            var result = await Call();

            result!.Status.Should().Be("Completed");
            result.Data!.Value.ValueKind.Should().Be(JsonValueKind.Array);
            result.Data.Value.GetArrayLength().Should().Be(0);
        }

        [Fact] // H11
        public async Task Last_mode_with_response_data_first_returns_the_first_item()
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", "first" } });
            InProcessRun(WorkflowExecutionStatus.Completed, Ran("hook", 1), Ran("code", 2));
            Outputs("code-run", new BsonDocument("a", 1), new BsonDocument("a", 2));

            var result = await Call();

            result!.Data!.Value.GetRawText().Replace(" ", "").Should().Be("{\"a\":1}");
        }

        [Fact] // H12
        public async Task Blocks_authentication_with_missing_response_mode_authenticates_then_queues()
        {
            Published(new BsonDocument { { "authType", "blocksAuthentication" } });
            _auth.Setup(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), "tenant-1")).ReturnsAsync(true);

            (await Call())!.Status.Should().Be("Queued");

            _auth.Verify(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), "tenant-1"), Times.Once);
            VerifyQueuedOnce();
        }

        // ---- critical path ----

        public static TheoryData<string, BsonDocument?> NoAuthType => new()
        {
            { "missing", new BsonDocument { { "httpResponseMode", "immediate" }, { "httpResponseData", "all" } } },
            { "empty document", new BsonDocument() },
            { "bson null", new BsonDocument { { "authType", BsonNull.Value } } },
            { "number", new BsonDocument { { "authType", 5 } } },
            { "parameters null", null },
        };

        [Theory] // C1, C1a — production route
        [MemberData(nameof(NoAuthType))]
        public async Task Webhook_without_a_string_authType_is_refused_with_401(string _, BsonDocument? parameters)
        {
            Published(parameters);

            // An invalid body too: the refusal comes first, so the body is never validated.
            var act = () => Call("5");

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            VerifyNoAuthCalls();
            VerifyNothingCreated();
            VerifyWarnings(Times.Once());
        }

        [Theory] // C1a — test route
        [MemberData(nameof(NoAuthType))]
        public async Task Test_webhook_without_a_string_authType_is_refused_with_401(string _, BsonDocument? parameters)
        {
            Draft(parameters);

            var act = () => CallTest("5");

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            VerifyNoAuthCalls();
            VerifyNothingCreated();
            VerifyWarnings(Times.Once());
        }

        public static TheoryData<BsonValue> NonStringValues => new()
        {
            BsonNull.Value,
            5,
            new BsonDocument("x", 1),
            new BsonArray { 1 },
        };

        [Theory] // C2 — response mode
        [MemberData(nameof(NonStringValues))]
        public async Task Non_string_response_mode_is_treated_as_immediate(BsonValue value)
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", value } });

            (await Call())!.Status.Should().Be("Queued");

            VerifyQueuedOnce();
        }

        [Theory] // C2 — response data
        [MemberData(nameof(NonStringValues))]
        public async Task Non_string_response_data_is_treated_as_all(BsonValue value)
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", value } });
            InProcessRun(WorkflowExecutionStatus.Completed, Ran("hook", 1), Ran("code", 2));
            Outputs("code-run", new BsonDocument("a", 1), new BsonDocument("a", 2));

            var result = await Call();

            result!.Data!.Value.GetRawText().Replace(" ", "").Should().Be("[{\"a\":1},{\"a\":2}]");
        }

        [Theory] // C3
        [InlineData(true)]
        [InlineData(false)]
        public async Task Unauthenticated_caller_is_refused_whether_or_not_response_settings_exist(bool withResponseSettings)
        {
            var parameters = new BsonDocument { { "authType", "blocksAuthentication" } };
            if (withResponseSettings)
            {
                parameters["httpResponseMode"] = "last";
                parameters["httpResponseData"] = "all";
            }
            Published(parameters);
            _auth.Setup(a => a.IsAuthenticated(It.IsAny<HttpRequest>(), "tenant-1")).ReturnsAsync(false);

            await ((Func<Task>)(() => Call())).Should().ThrowAsync<UnauthorizedAccessException>();

            VerifyNothingCreated();
        }

        [Fact] // C4
        public async Task Blocks_authorization_with_no_authorization_block_is_refused()
        {
            Published(new BsonDocument { { "authType", "blocksAuthorization" } });

            await ((Func<Task>)(() => Call())).Should().ThrowAsync<UnauthorizedAccessException>();

            VerifyNoAuthCalls();
            VerifyNothingCreated();
        }

        [Fact] // C5
        public async Task Invalid_body_with_missing_response_settings_is_a_400_and_creates_nothing()
        {
            Published(new BsonDocument { { "authType", "none" } });

            await ((Func<Task>)(() => Call("5"))).Should().ThrowAsync<InvalidWebhookBodyException>();

            VerifyNothingCreated();
        }

        [Fact] // C6
        public async Task Unknown_webhook_id_returns_workflow_not_found()
        {
            Published(new BsonDocument { { "authType", "none" } });

            var result = await _sut.TriggerWebhookAsync("wf-1", "other", "tenant-1", Json("{}"));

            result!.Status.Should().Be("Workflow not found");
            result.ExecutionId.Should().BeNull();
            VerifyNothingCreated();
        }

        [Fact] // C7
        public async Task Test_webhook_when_not_listening_returns_the_not_listening_status()
        {
            Draft(new BsonDocument { { "authType", "none" } }, listening: false);

            var result = await CallTest();

            result!.Status.Should().Contain("is not listening for test executions");
            VerifyNothingCreated();
        }

        [Fact] // C8b, C11
        public async Task Unrecognised_response_mode_with_missing_response_data_queues_once()
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "sometimes" } });

            (await Call())!.Status.Should().Be("Queued");

            VerifyQueuedOnce();
        }

        [Fact] // C9
        public async Task Last_mode_with_response_data_none_returns_null()
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" }, { "httpResponseData", "none" } });
            InProcessRun(WorkflowExecutionStatus.Completed, Ran("hook", 1), Ran("code", 2));

            (await Call()).Should().BeNull();
        }

        [Fact] // C10
        public async Task Failed_last_mode_run_with_missing_response_data_reports_failed_and_all_items()
        {
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" } });
            _engine.Setup(e => e.RunNodeInProcessAsync(It.IsAny<AddExcuationNodeEvent>()))
                .ReturnsAsync(RunResult(WorkflowExecutionStatus.Failed, "internal details", [Ran("hook", 1), Ran("code", 2)]));
            Outputs("code-run", new BsonDocument { { "error", true }, { "message", "boom" } });

            var result = await Call();

            result!.Status.Should().Be("Failed");
            result.Data!.Value.GetRawText().Should().Contain("boom").And.NotContain("internal details");
        }

        [Fact] // C12
        public async Task Last_mode_releases_the_grant_when_the_run_throws_with_missing_response_data()
        {
            const string grant = "dg_grant";
            Published(new BsonDocument { { "authType", "none" }, { "httpResponseMode", "last" } });
            _grants.Setup(g => g.CreateForSendAsync(It.IsAny<TimeSpan?>())).ReturnsAsync(grant);
            _engine.Setup(e => e.RunNodeInProcessAsync(It.IsAny<AddExcuationNodeEvent>()))
                .ThrowsAsync(new InvalidOperationException("engine failed"));

            await ((Func<Task>)(() => Call())).Should().ThrowAsync<InvalidOperationException>();

            _releaser.Verify(r => r.ReleaseAsync(grant), Times.Once);
            DelegatedTokenContext.Current.Should().BeNull();
        }
    }
}
