using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionProxy;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Covers the Proxy action node's own job: turning node parameters plus an input item into one
    /// <see cref="ProxyForwardRequest"/>, and turning the relayed response back into output items.
    /// <see cref="IProxyGatewayService"/> is mocked throughout - route matching, secret resolution and the
    /// upstream call are the gateway's own tests, not this node's.
    /// </summary>
    public class ActionProxyNodeTests : IDisposable
    {
        private readonly Mock<IProxyGatewayService> _gateway = new();
        private readonly List<ProxyForwardRequest> _sent = new();

        public ActionProxyNodeTests()
        {
            TestBlocksContext.Set("tenant-1", "user-1");
            Relay("{\"ok\":true}");
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        // ----- Helpers -------------------------------------------------------------

        /// <summary>Makes the gateway relay <paramref name="body"/> and capture every request it is given.</summary>
        private void Relay(string body) => Setup(new ProxyForwardResult
        {
            StatusCode = 200,
            Outcome = ProxyExecutionOutcome.Success,
            ResponseBody = body,
            ResponseContentType = "application/json",
        });

        private void Setup(ProxyForwardResult result)
        {
            _gateway.Reset();
            _sent.Clear();
            _gateway
                .Setup(g => g.ForwardAsync(It.IsAny<ProxyForwardRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ProxyForwardRequest, CancellationToken>((request, _) => _sent.Add(request))
                .ReturnsAsync(result);
        }

        private ActionProxyNode Node() => new(_gateway.Object, NullLogger<ActionProxyNode>.Instance);

        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "upstream-node",
            NodeExecutionId = "ne-1",
            NodeName = "Upstream",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output },
        };

        private static NodeExecutionContext Context(
            BsonDocument parameters,
            List<WorkflowItemExecutionEntity>? items = null,
            bool hasUpstream = true)
        {
            items ??= new List<WorkflowItemExecutionEntity> { Item("item-1", new BsonDocument()) };
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                WorkflowId = "wf-1",
                NodeId = "proxy-node",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                HasUpstream = hasUpstream,
            };
        }

        private static BsonDocument Parameters(
            string routePath = "",
            string method = "GET",
            BsonDocument? pathParams = null,
            BsonDocument? queryParams = null,
            bool haveQuery = false,
            bool haveBody = false,
            string body = "")
            => new()
            {
                { "proxyId", "proxy-1" },
                { "slug", "billing" },
                { "routeMethod", method },
                { "routePath", routePath },
                { "pathParams", pathParams ?? new BsonDocument() },
                { "haveQuery", haveQuery },
                { "queryParams", queryParams ?? new BsonDocument() },
                { "havebody", haveBody },
                { "body", body },
            };

        // ----- Metadata ------------------------------------------------------------

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = Node();
            node.NodeType.Should().Be("proxy");
            node.Version.Should().Be("v1");
        }

        // ----- Required selections --------------------------------------------------

        [Fact]
        public async Task RunAsync_NoProxySelected_FailsWithoutCallingTheGateway()
        {
            var parameters = Parameters();
            parameters["slug"] = "";

            var result = await Node().RunAsync(Context(parameters));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("No proxy is selected on this node.");
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_NoEndpointSelected_FailsWithoutCallingTheGateway()
        {
            var result = await Node().RunAsync(Context(Parameters(method: "")));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("No endpoint is selected on this node.");
            _sent.Should().BeEmpty();
        }

        // ----- Path parameters ------------------------------------------------------

        [Fact]
        public async Task RunAsync_PathParameter_IsSubstitutedRawSoTheGatewayEncodesItOnce()
        {
            // The gateway percent-encodes per segment when it builds the outbound URL, exactly as it does
            // for routing's decoded {**path}. Encoding here too would send "John%2520Doe" upstream.
            var parameters = Parameters(
                routePath: "customers/{name}/invoices",
                pathParams: new BsonDocument { { "name", "John Doe" } });

            var result = await Node().RunAsync(Context(parameters));

            result.IsSuccess.Should().BeTrue();
            _sent.Single().PathSuffix.Should().Be("customers/John Doe/invoices");
        }

        [Fact]
        public async Task RunAsync_PathParameterFromExpression_IsResolvedPerItem()
        {
            var parameters = Parameters(
                routePath: "orders/{id}",
                pathParams: new BsonDocument { { "id", "{{$json.output.orderId}}" } });
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("item-1", new BsonDocument { { "orderId", "A1" } }),
                Item("item-2", new BsonDocument { { "orderId", "B2" } }),
            };

            var result = await Node().RunAsync(Context(parameters, items));

            result.IsSuccess.Should().BeTrue();
            _sent.Select(r => r.PathSuffix).Should().Equal("orders/A1", "orders/B2");
        }

        [Fact]
        public async Task RunAsync_PathParameterContainingSlash_FailsInsteadOfWideningThePath()
        {
            var parameters = Parameters(
                routePath: "orders/{id}",
                pathParams: new BsonDocument { { "id", "1/refunds" } });

            var result = await Node().RunAsync(Context(parameters));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("cannot contain");
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_PathParameterDotSegment_Fails()
        {
            var parameters = Parameters(
                routePath: "orders/{id}",
                pathParams: new BsonDocument { { "id", ".." } });

            var result = await Node().RunAsync(Context(parameters));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not valid");
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_MissingPathParameter_FailsNamingIt()
        {
            var result = await Node().RunAsync(Context(Parameters(routePath: "orders/{id}")));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("id");
            _sent.Should().BeEmpty();
        }

        // ----- Query parameters -----------------------------------------------------

        [Fact]
        public async Task RunAsync_QueryParameters_AreSentPercentEncoded()
        {
            var parameters = Parameters(
                haveQuery: true,
                queryParams: new BsonDocument { { "status", "past due" }, { "page", "2" } });

            var result = await Node().RunAsync(Context(parameters));

            result.IsSuccess.Should().BeTrue();
            _sent.Single().IncomingQuery.Should().Be("status=past%20due&page=2");
        }

        [Fact]
        public async Task RunAsync_QueryParametersSwitchedOff_SendsNoQuery()
        {
            var parameters = Parameters(
                haveQuery: false,
                queryParams: new BsonDocument { { "page", "2" } });

            await Node().RunAsync(Context(parameters));

            _sent.Single().IncomingQuery.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_QueryParameterResolvingToEmpty_IsDropped()
        {
            var parameters = Parameters(
                haveQuery: true,
                queryParams: new BsonDocument { { "cursor", "{{$json.output.cursor}}" }, { "page", "1" } });
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("item-1", new BsonDocument { { "cursor", "" } }),
            };

            await Node().RunAsync(Context(parameters, items));

            _sent.Single().IncomingQuery.Should().Be("page=1");
        }

        // ----- Body -----------------------------------------------------------------

        [Fact]
        public async Task RunAsync_InvalidJsonBody_FailsBeforeAnyUpstreamCall()
        {
            var parameters = Parameters(method: "POST", haveBody: true, body: "{ not json");

            var result = await Node().RunAsync(Context(parameters));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().StartWith("Invalid JSON body:");
            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_BodyOnAMethodThatCarriesNone_IsNotSent()
        {
            var parameters = Parameters(method: "GET", haveBody: true, body: "{\"a\":1}");

            await Node().RunAsync(Context(parameters));

            _sent.Single().Body.Should().BeNull();
        }

        // ----- Forward provenance ---------------------------------------------------

        [Fact]
        public async Task RunAsync_AttributesTheCallToTheWorkflowRunAndNode()
        {
            await Node().RunAsync(Context(Parameters()));

            var sent = _sent.Single();
            sent.CallerKind.Should().Be(ProxyCallerKind.Workflow);
            sent.TenantId.Should().Be("tenant-1");
            sent.WorkflowId.Should().Be("wf-1");
            sent.WorkflowRunId.Should().Be("exec-1");
            sent.WorkflowNodeId.Should().Be("proxy-node");
            sent.Slug.Should().Be("billing");
            sent.RequestPath.Should().Be("/api/proxy/gateway/billing");
        }

        // ----- Iteration ------------------------------------------------------------

        [Fact]
        public async Task RunAsync_NoInputAndNoUpstream_CallsTheProxyExactlyOnce()
        {
            var context = Context(Parameters(), new List<WorkflowItemExecutionEntity>(), hasUpstream: false);

            var result = await Node().RunAsync(context);

            result.IsSuccess.Should().BeTrue();
            _sent.Should().ContainSingle();
            result.OutputItems.Should().ContainSingle();
            // Nothing produced this run, so it claims no parent: the engine reads these ids back out of
            // InputItems to build the ancestor map.
            result.OutputItems[0].ParentItemIds.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_NoInputButHasUpstream_DoesNothing()
        {
            // An untaken branch: the engine dispatches down every outgoing edge and relies on the
            // zero-item node to prune. Calling the third party here would defeat the IF that skipped it.
            var context = Context(Parameters(), new List<WorkflowItemExecutionEntity>(), hasUpstream: true);

            var result = await Node().RunAsync(context);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
            _sent.Should().BeEmpty();
        }

        // ----- Response shaping -----------------------------------------------------

        [Fact]
        public async Task RunAsync_ArrayResponse_BecomesOneOutputItemPerElement()
        {
            Relay("[{\"id\":1},{\"id\":2}]");

            var result = await Node().RunAsync(Context(Parameters()));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["id"].AsInt32.Should().Be(1);
            result.OutputItems[1].Data.Output["id"].AsInt32.Should().Be(2);
            result.OutputItems.Should().OnlyContain(i => i.Branch == "source");
        }

        [Fact]
        public async Task RunAsync_EmptyResponseBody_StillProducesOneItem()
        {
            Relay(string.Empty);

            var result = await Node().RunAsync(Context(Parameters()));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().ContainSingle();
        }

        [Fact]
        public async Task RunAsync_NonJsonResponse_FailsNamingTheContentType()
        {
            Setup(new ProxyForwardResult
            {
                StatusCode = 200,
                Outcome = ProxyExecutionOutcome.Success,
                ResponseBody = "<html/>",
                ResponseContentType = "text/html",
            });

            var result = await Node().RunAsync(Context(Parameters()));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("text/html");
        }

        // ----- Gateway refusals -----------------------------------------------------

        [Fact]
        public async Task RunAsync_RouteNotAllowed_TellsTheUserToReselectTheEndpoint()
        {
            Setup(new ProxyForwardResult
            {
                StatusCode = 403,
                Outcome = ProxyExecutionOutcome.RouteNotAllowed,
            });

            var result = await Node().RunAsync(Context(Parameters(routePath: "orders")));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("does not declare the endpoint GET orders");
            result.ErrorMessage.Should().Contain("re-select the endpoint");
        }

        [Fact]
        public async Task RunAsync_MethodNotAllowed_NamesWhatTheProxyAllows()
        {
            Setup(new ProxyForwardResult
            {
                StatusCode = 405,
                Outcome = ProxyExecutionOutcome.MethodNotAllowed,
                AllowedMethods = new List<string> { "GET", "POST" },
            });

            var result = await Node().RunAsync(Context(Parameters(method: "DELETE")));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("does not allow DELETE");
            result.ErrorMessage.Should().Contain("GET, POST");
        }

        [Fact]
        public async Task RunAsync_UpstreamFailure_ReportsTheUpstreamStatus()
        {
            Setup(new ProxyForwardResult
            {
                StatusCode = 502,
                Outcome = ProxyExecutionOutcome.UpstreamUnreachable,
                UpstreamStatusCode = 503,
                ErrorMessage = "Could not connect to the upstream endpoint",
            });

            var result = await Node().RunAsync(Context(Parameters()));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("503");
            result.ErrorMessage.Should().Contain("Could not connect to the upstream endpoint");
        }
    }
}
