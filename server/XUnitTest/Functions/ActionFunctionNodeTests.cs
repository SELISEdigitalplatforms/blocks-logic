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
            public List<(string TenantId, string FunctionId, string? Input, int? WaitTimeoutSeconds, string? WorkflowExecutionId)> Calls { get; } = [];
            public Func<string?, InvokeResultDto> Respond { get; set; } = _ => new InvokeResultDto
            {
                RunId = "run_1", Status = "SUCCEEDED", Result = "{\"ok\":true}",
            };
            public Exception? ThrowOnInvoke { get; set; }

            public Task<InvokeResultDto> InvokeFromWorkflowAsync(
                string tenantId, string functionId, string? inputJson, BlocksContext? callerContext,
                int? waitTimeoutSeconds, string? workflowExecutionId, CancellationToken cancellationToken = default)
            {
                if (ThrowOnInvoke is not null) throw ThrowOnInvoke;
                Calls.Add((tenantId, functionId, inputJson, waitTimeoutSeconds, workflowExecutionId));
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
            string inputMode = "item", string? inputExpression = null, int? waitTimeoutSec = null,
            bool hasUpstream = true, CancellationToken cancellationToken = default)
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
                HasUpstream = hasUpstream,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
                CancellationToken = cancellationToken,
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

        [Theory]
        [InlineData("{\"a\":1,\"b\":\"x\",\"c\":{\"d\":true}}", "{\"a\":1,\"b\":\"x\",\"c\":{\"d\":true}}")]
        [InlineData("[1,2,{\"e\":null}]", "[1,2,{\"e\":null}]")]
        [InlineData("42", "42")]
        [InlineData("\"plain text\"", "\"plain text\"")]
        public async Task Custom_json_input_reaches_the_function_as_written(string expression, string expected)
        {
            // WF-11: the expression parser returns Newtonsoft tokens; System.Text.Json wrote an
            // object as nested empty arrays ({"a":1} arrived as {"a":[]}).
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument("orderId", "42")) };

            await Node(service).RunAsync(Context(items, inputMode: "expression", inputExpression: expression));

            service.Calls[0].Input.Should().Be(expected);
        }

        // ----- Expression input is a JSON template (PKG-20 / PKG-23) -------------------------

        [Fact]
        public async Task Plain_text_input_that_is_not_json_fails_the_step_and_calls_nothing()
        {
            // Was: silently sent as the JSON string "plain text".
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items, inputMode: "expression", inputExpression: "plain text"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().StartWith("Input is not valid JSON after filling in values:")
                .And.Contain("line 1, position").And.EndWith("(item 1)");
            service.Calls.Should().BeEmpty();
        }

        [Theory]
        [InlineData("{{$json.output.email}}")]
        [InlineData("{{$json.email}}")]
        [InlineData("  {{ $json.email }}  ")]
        public async Task A_plain_expression_input_becomes_a_json_string(string expression)
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument("email", "a\"b@x.io")) };

            var result = await Node(service).RunAsync(Context(items, inputMode: "expression", inputExpression: expression));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            service.Calls[0].Input.Should().Be("\"a\\\"b@x.io\"");
        }

        [Fact]
        public async Task Values_in_a_json_input_template_are_escaped_and_typed()
        {
            var service = new FakeInvocationService();
            var output = new BsonDocument
            {
                { "name", "Bob \"B\" Smith" },
                { "zip", "123" },
                { "n", 5 },
                { "tags", new BsonArray { "a", "b" } },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", output) };

            var result = await Node(service).RunAsync(Context(items, inputMode: "expression",
                inputExpression: "{\"label\": \"Hi {{$json.name}}\", \"name\": {{$json.name}}, \"zip\": {{$json.zip}}, \"n\": {{$json.n}}, \"tags\": {{$json.tags}}}"));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            using var doc = System.Text.Json.JsonDocument.Parse(service.Calls[0].Input!);
            doc.RootElement.GetProperty("label").GetString().Should().Be("Hi Bob \"B\" Smith");
            doc.RootElement.GetProperty("name").GetString().Should().Be("Bob \"B\" Smith");
            doc.RootElement.GetProperty("zip").GetString().Should().Be("123");
            doc.RootElement.GetProperty("n").GetInt32().Should().Be(5);
            doc.RootElement.GetProperty("tags").GetArrayLength().Should().Be(2);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{{$json.missing}}")]
        public async Task A_blank_or_null_input_sends_no_input(string expression)
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items, inputMode: "expression", inputExpression: expression));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            service.Calls.Should().ContainSingle().Which.Input.Should().BeNull();
        }

        [Fact]
        public async Task Invalid_input_on_a_later_item_fails_the_step_and_keeps_the_items_already_run()
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("i1", new BsonDocument("v", 5)),
                Item("i2", new BsonDocument("v", "x")),
            };

            // Item 1 fills to {"a": 15} (valid); item 2 to {"a": 1"x"} (invalid).
            var result = await Node(service).RunAsync(Context(items, inputMode: "expression",
                inputExpression: "{\"a\": 1{{$json.v}}}"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Input is not valid JSON").And.EndWith("(item 2)");
            service.Calls.Should().ContainSingle().Which.Input.Should().Be("{\"a\": 15}");
            result.OutputItems.Should().ContainSingle().Which.ParentItemIds.Should().BeEquivalentTo(["i1"]);
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
        public async Task A_run_the_step_stopped_waiting_for_is_not_called_failed()
        {
            // It may still finish, or may already have done its work: the text must say so, so
            // nobody re-runs the workflow believing nothing happened.
            var service = new FakeInvocationService
            {
                Respond = _ => new InvokeResultDto { RunId = "run_9", Status = "RUNNING" },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("run_9").And.Contain("did not finish in time")
                .And.Contain("may already have done its work").And.NotContain("did not succeed");
        }

        [Fact]
        public async Task When_an_item_fails_the_items_before_it_are_kept()
        {
            // Items 1 and 2 really ran (side effects included); dropping them hid that.
            var calls = 0;
            var service = new FakeInvocationService
            {
                Respond = _ => ++calls == 3
                    ? new InvokeResultDto { RunId = "run_3", Status = "FAILED", ErrorMessage = "card declined" }
                    : new InvokeResultDto { RunId = "run_ok", Status = "SUCCEEDED", Result = "{\"charged\":true}" },
            };
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("i1", new BsonDocument("n", 1)),
                Item("i2", new BsonDocument("n", 2)),
                Item("i3", new BsonDocument("n", 3)),
                Item("i4", new BsonDocument("n", 4)),
            };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeFalse();
            service.Calls.Should().HaveCount(3, "the step stops at the failing item");
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems!.Select(o => o.ParentItemIds![0]).Should().Equal("i1", "i2");
            result.ErrorMessage.Should().Contain("item 3 of 4").And.Contain("card declined");
        }

        [Fact]
        public async Task On_resume_items_that_already_succeeded_are_not_called_again()
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("i1", new BsonDocument("n", 1)), Item("i2", new BsonDocument("n", 2)), Item("i3", new BsonDocument("n", 3)),
            };
            var done = Item("prev-1", new BsonDocument());
            done.ParentItemIds = ["i1"];
            done.Data = new NodeOutputItemData { Output = new BsonDocument("charged", "before") };
            var context = Context(items);
            context = new NodeExecutionContext
            {
                WorkflowExecutionId = context.WorkflowExecutionId, TenantId = context.TenantId, Parameters = context.Parameters,
                InputItems = context.InputItems, IterationCount = context.IterationCount, HasUpstream = true,
                WorkflowContext = context.WorkflowContext, AncestorNodeOutputs = context.AncestorNodeOutputs,
                PreviousAttemptItems = [done],
            };

            var result = await Node(service).RunAsync(context);

            result.IsSuccess.Should().BeTrue();
            service.Calls.Should().HaveCount(2, "item 1 succeeded before and is not charged again");
            result.OutputItems.Should().HaveCount(3);
            result.OutputItems[0].Data.Output["charged"].AsString.Should().Be("before");
            result.OutputItems[0].ParentItemIds.Should().Equal("i1");
        }

        [Fact]
        public async Task When_the_invocation_throws_the_items_before_it_are_kept()
        {
            var calls = 0;
            var service = new FakeInvocationService
            {
                Respond = _ =>
                {
                    if (++calls == 2) throw new InvalidOperationException("this function requires authentication");
                    return new InvokeResultDto { RunId = "run_ok", Status = "SUCCEEDED", Result = "1" };
                },
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()), Item("i2", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items));

            result.IsSuccess.Should().BeFalse();
            result.OutputItems.Should().ContainSingle();
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
        public async Task Zero_items_from_an_upstream_invokes_nothing_and_succeeds_with_an_empty_result()
        {
            // An untaken branch: the engine dispatches down every outgoing edge and relies on the
            // zero-item node to prune. Firing here would run the function on a path the workflow
            // deliberately did not choose.
            var service = new FakeInvocationService();

            var result = await Node(service).RunAsync(Context([], hasUpstream: true));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
            service.Calls.Should().BeEmpty();
        }

        [Fact]
        public async Task A_node_with_nothing_wired_to_it_still_invokes_once()
        {
            // Testing a lone function step is the first thing anyone does after dropping one on the
            // canvas. Zero iterations would make it succeed without ever calling the function.
            var service = new FakeInvocationService();

            var result = await Node(service).RunAsync(Context([], hasUpstream: false));

            result.IsSuccess.Should().BeTrue();
            service.Calls.Should().ContainSingle();
            result.OutputItems.Should().ContainSingle();
            result.OutputItems[0].ParentItemIds.Should().BeEmpty();
        }

        [Fact]
        public async Task A_standalone_run_sends_no_input_rather_than_failing_on_the_missing_item()
        {
            var service = new FakeInvocationService();

            await Node(service).RunAsync(Context([], hasUpstream: false));

            service.Calls[0].Input.Should().Be("{ }");
        }

        [Fact]
        public async Task The_workflow_execution_id_is_passed_through_as_the_runs_invoker()
        {
            // Spec §41: ctx.run.invokedBy must identify the workflow execution. Without it a run
            // shows "Triggered by workflow" and names no workflow at all.
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            await Node(service).RunAsync(Context(items));

            service.Calls[0].WorkflowExecutionId.Should().Be("exec-1");
        }

        /// <summary>
        /// The step waits exactly as long as the run may take, and a stored override is ignored.
        /// <para>
        /// It used to be editable. A step that waits for less time than the run it started does not
        /// cancel anything — the run executes and fires its output actions regardless — it only
        /// stops being there to collect the result, which is a side effect with nothing to show for
        /// it. Waiting longer achieves nothing either, since the run cannot outlive its own timeout.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData(12)]
        [InlineData(0)]
        [InlineData(-30)]
        public async Task A_stored_wait_timeout_is_ignored_and_the_step_waits_for_the_run(int? waitTimeoutSec)
        {
            var service = new FakeInvocationService();
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var result = await Node(service).RunAsync(Context(items, waitTimeoutSec: waitTimeoutSec));

            result.IsSuccess.Should().BeTrue();
            service.Calls.Should().ContainSingle()
                .Which.WaitTimeoutSeconds.Should().BeNull("the run's own timeout is the wait");
        }

        [Fact]
        public async Task Cancellation_propagates_instead_of_becoming_a_failed_step()
        {
            // The engine stopping this execution is not the function failing, and recording it as a
            // step error would blame the tenant's code for something the workflow asked for.
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            var service = new FakeInvocationService
            {
                ThrowOnInvoke = new OperationCanceledException(cts.Token),
            };
            var items = new List<WorkflowItemExecutionEntity> { Item("i1", new BsonDocument()) };

            var act = async () => await Node(service).RunAsync(Context(items, cancellationToken: cts.Token));

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
