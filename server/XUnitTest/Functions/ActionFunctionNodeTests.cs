using Blocks.Genesis;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Nodes;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The workflow's own entry point into a deployed function. Per spec: one synchronous
    /// invocation per input item, the returned value becomes the output item, and a run that
    /// does not succeed fails the whole step — a workflow has no notion of "partially ran".
    /// </summary>
    public class ActionFunctionNodeTests
    {
        private sealed class FakeInvocationService : IFunctionInvocationService
        {
            public List<(string TenantId, string FunctionId, string? Input)> Calls { get; } = [];
            public Func<string?, InvokeResultDto> Respond { get; set; } = _ => new InvokeResultDto
            {
                RunId = "run_1", Status = "SUCCEEDED", Result = "{\"ok\":true}",
            };
            public Exception? ThrowOnInvoke { get; set; }

            public Task<InvokeResultDto> InvokeFromWorkflowAsync(
                string tenantId, string functionId, string? inputJson, BlocksContext? callerContext,
                int? waitTimeoutSeconds, CancellationToken cancellationToken = default)
            {
                if (ThrowOnInvoke is not null) throw ThrowOnInvoke;
                Calls.Add((tenantId, functionId, inputJson));
                return Task.FromResult(Respond(inputJson));
            }

            public Task<InvokeResultDto> InvokeHttpAsync(string tenantId, string functionId, InvokeFunctionRequestDto request, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task<InvokeResultDto> TestAsync(string tenantId, string functionId, TestFunctionRequestDto request, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task<InvokeResultDto> ReplayAsync(string tenantId, FunctionRunEntity originalRun, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }

        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant_1",
            NodeId = "node-1",
            NodeExecutionId = "ne-1",
            NodeName = "Function",
            Branch = "source",
            ItemIndex = 0,
            ParentItemIds = [],
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output },
        };

        private static NodeExecutionContext Context(
            List<WorkflowItemExecutionEntity> items, string functionId = "fn_1",
            string inputMode = "item", string? inputExpression = null, int? waitTimeoutSec = null)
        {
            var parameters = new BsonDocument
            {
                { "FunctionId", functionId },
                { "InputMode", inputMode },
                { "InputExpression", inputExpression ?? string.Empty },
                { "WaitTimeoutSec", waitTimeoutSec.HasValue ? BsonValue.Create(waitTimeoutSec.Value) : BsonNull.Value },
            };
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant_1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };
        }

        private static ActionFunctionNode Node(FakeInvocationService service) =>
            new(service, NullLogger<ActionFunctionNode>.Instance);

        [Fact]
        public void NodeMetadata_is_function_v1()
        {
            var node = Node(new FakeInvocationService());
            node.NodeType.Should().Be("function");
            node.Version.Should().Be("v1");
        }

        [Fact]
        public async Task Invokes_once_per_input_item()
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("i1", new BsonDocument("a", 1)),
                Item("i2", new BsonDocument("a", 2)),
            };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeTrue();
            service.Calls.Should().HaveCount(2);
        }

        [Fact]
        public async Task Item_mode_passes_the_current_items_own_output_as_input()
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument("orderId", "42")) };

            await Node(service).RunAsync(Context(items, inputMode: "item"));

            service.Calls[0].Input.Should().Contain("42").And.Contain("orderId");
        }

        [Fact]
        public async Task The_returned_value_becomes_the_output_items_data()
        {
            var service = new FakeInvocationService
            {
                Respond = _ => new InvokeResultDto { RunId = "run_1", Status = "SUCCEEDED", Result = "{\"total\":7}" },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.OutputItems.Should().ContainSingle();
            result.OutputItems[0].Data.Output["total"].AsInt32.Should().Be(7);
            result.OutputItems[0].ParentItemIds.Should().BeEquivalentTo(["i1"]);
        }

        [Fact]
        public async Task A_run_that_does_not_succeed_fails_the_whole_step()
        {
            // A workflow has no notion of "partially ran" — one bad item stops the chain.
            var service = new FakeInvocationService
            {
                Respond = _ => new InvokeResultDto
                {
                    RunId = "run_1", Status = "FAILED", ErrorCode = "USER_RUNTIME_ERROR", ErrorMessage = "boom",
                },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("boom");
        }

        [Fact]
        public async Task A_still_running_result_after_the_wait_window_is_also_a_failure()
        {
            // A synchronous workflow step cannot return "pending" — see
            // FunctionInvocationService.WaitForResultAsync, which reports RUNNING rather than
            // blocking forever once its wait window lapses.
            var service = new FakeInvocationService
            {
                Respond = _ => new InvokeResultDto { RunId = "run_1", Status = "RUNNING" },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task An_exception_from_the_invocation_service_fails_the_step_with_its_message()
        {
            var service = new FakeInvocationService { ThrowOnInvoke = new InvalidOperationException("no such function") };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("no such function");
        }

        [Fact]
        public async Task No_function_selected_fails_without_calling_the_service()
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items, functionId: ""));

            result.IsSuccess.Should().BeFalse();
            service.Calls.Should().BeEmpty();
        }

        [Fact]
        public async Task A_missing_result_value_becomes_an_explicit_null_output_not_a_crash()
        {
            var service = new FakeInvocationService
            {
                Respond = _ => new InvokeResultDto { RunId = "run_1", Status = "SUCCEEDED", Result = null },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems[0].Data.Output.Should().Be(BsonNull.Value);
        }

        [Fact]
        public async Task Zero_items_invokes_nothing_and_succeeds_with_an_empty_result()
        {
            var service = new FakeInvocationService();

            var result = await Node(service).RunAsync(Context([]));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
            service.Calls.Should().BeEmpty();
        }
    }
}
