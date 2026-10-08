using System.Net;
using System.Text;
using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionAIAgentV1;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// AI Agent node: an agent failure is an error item with the real cause (like the HTTP Request node).
    /// It used to be swallowed (Console.WriteLine + empty JsonElement) and the item only said
    /// "Operation is not valid due to the current state of the object."
    /// </summary>
    public class ActionAIAgentV1NodeTests
    {
        private sealed class FakeAgentNode : ActionAIAgentV1Node
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public List<HttpRequestMessage> Requests { get; } = new();

            public FakeAgentNode(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion)
            {
                Requests.Add(request);
                return Task.FromResult(_respond(request));
            }
        }

        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        private const string Initiate = """{"session_id":"s-1","token":"t-1"}""";

        private static Func<HttpRequestMessage, HttpResponseMessage> Agent(string initiate, string stream, HttpStatusCode chatStatus = HttpStatusCode.OK)
            => request => request.Method == HttpMethod.Get
                ? Ok(initiate)
                : new HttpResponseMessage(chatStatus) { Content = new StringContent(stream) };

        private static NodeExecutionContext Context(string widgetId = "w-1", string apiBaseUrl = "https://agent.test") => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = new BsonDocument
            {
                { "widgetId", widgetId },
                { "apiBaseUrl", apiBaseUrl },
                { "input", "hello" },
            },
            InputItems = new List<WorkflowItemExecutionEntity>
            {
                new()
                {
                    Id = "item-1", WorkflowExecutionId = "exec-1", TenantId = "tenant-1", NodeId = "t",
                    NodeExecutionId = "ne-1", NodeName = "Trigger", Branch = "source",
                    ParentItemIds = new List<string>(), AncestorMap = new Dictionary<string, string>(),
                    Data = new NodeOutputItemData { Output = new BsonDocument() },
                },
            },
            IterationCount = 1,
            WorkflowContext = new BsonDocument(),
        };

        private static string ErrorMessage(NodeExecutionResult result)
        {
            result.IsSuccess.Should().BeTrue("an agent error is an error item, like the HTTP Request node");
            var output = result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;
            output["error"].AsBoolean.Should().BeTrue();
            return output["message"].AsString;
        }

        [Fact]
        public async Task Reply_becomes_the_output()
        {
            var node = new FakeAgentNode(Agent(Initiate, "event: chat_response\ndata: {\"message\":\"hi there\"}\n\n"));

            var result = await node.RunAsync(Context());

            result.IsSuccess.Should().BeTrue();
            var item = result.OutputItems.Should().ContainSingle().Subject;
            item.Data.Output.AsString.Should().Be("hi there");
            item.Branch.Should().Be("source");
        }

        [Fact]
        public async Task Initiate_http_error_is_an_error_item_with_the_status()
        {
            var node = new FakeAgentNode(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var message = ErrorMessage(await node.RunAsync(Context()));

            message.Should().Contain("starting the conversation").And.Contain("401");
        }

        [Fact]
        public async Task Chat_http_error_is_an_error_item_with_the_status()
        {
            var node = new FakeAgentNode(Agent(Initiate, "", HttpStatusCode.InternalServerError));

            var message = ErrorMessage(await node.RunAsync(Context()));

            message.Should().Contain("sending the message").And.Contain("500");
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("""{"session_id":""}""")]
        public async Task Bad_initiate_response_is_an_error_item(string initiate)
        {
            var node = new FakeAgentNode(Agent(initiate, ""));

            var message = ErrorMessage(await node.RunAsync(Context()));

            message.Should().StartWith("AI agent request failed");
        }

        [Theory]
        [InlineData("")]
        [InlineData("event: typing\ndata: {}\n")]
        [InlineData("event: chat_response\ndata: {\"other\":1}\n")]
        [InlineData("event: chat_response\n")]
        public async Task Stream_without_a_reply_is_an_error_item(string stream)
        {
            var node = new FakeAgentNode(Agent(Initiate, stream));

            var message = ErrorMessage(await node.RunAsync(Context()));

            message.Should().Contain("without a reply");
        }

        [Fact]
        public async Task Reply_that_is_not_json_is_an_error_item()
        {
            var node = new FakeAgentNode(Agent(Initiate, "event: chat_response\ndata: {oops\n"));

            var message = ErrorMessage(await node.RunAsync(Context()));

            message.Should().Contain("not valid JSON");
        }

        [Fact]
        public async Task Network_failure_is_an_error_item()
        {
            var node = new FakeAgentNode(_ => throw new HttpRequestException("connection refused"));

            var message = ErrorMessage(await node.RunAsync(Context()));

            message.Should().Contain("connection refused");
        }

        [Theory]
        [InlineData("", "https://agent.test", "no agent")]
        [InlineData("w-1", "", "no API base URL")]
        public async Task Missing_configuration_is_an_error_item_and_sends_nothing(string widgetId, string apiBaseUrl, string expected)
        {
            var node = new FakeAgentNode(Agent(Initiate, ""));

            var message = ErrorMessage(await node.RunAsync(Context(widgetId, apiBaseUrl)));

            message.Should().Contain(expected);
            node.Requests.Should().BeEmpty();
        }
    }
}
