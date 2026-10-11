using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.Json;
using Api.Controllers;
using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using MongoDB.Bson;
using Scheduler.DomainService.Services;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TransformCodeV1;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;
using FirebaseConfiguration = DomainService.Configuration.FirebaseConfiguration;

namespace XUnitTest.Security
{
    /// <summary>
    /// User rule 2026-10-08: "never ever show secret in log or store the value". Each test feeds a known
    /// secret through one path that used to log it and asserts the captured logs never contain it.
    /// </summary>
    public class SecretLogTests
    {
        private const string Secret = "s3cr3t-value-7a1c9e";

        // ----- Client credential token: the error body may echo the submitted form ---------------

        private sealed class EchoHandler(HttpStatusCode status, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        [Fact]
        public async Task A_failed_token_call_logs_the_error_code_but_never_the_body()
        {
            var body = $$"""{"error":"invalid_client","error_description":"bad client_secret={{Secret}}"}""";
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(new EchoHandler(HttpStatusCode.Unauthorized, body), disposeHandler: false));
            var logger = new CapturingLogger<ClientCredentialTokenService>();
            var sut = new ClientCredentialTokenService(logger, factory.Object, new ConfigurationBuilder().Build());

            var token = await sut.GetTokenAsync(new ClientCredential { ItemId = "client-1", ClientSecret = Secret }, "tenant-1");

            token.Should().BeNull();
            logger.All.Should().NotContain(Secret).And.Contain("invalid_client");
        }

        [Theory]
        [InlineData("""{"error":"invalid_grant"}""", "invalid_grant")]
        [InlineData("""{"error":"a secret with spaces s3cr3t"}""", "unknown")]
        [InlineData("not json s3cr3t", "unknown")]
        [InlineData("", "none")]
        [InlineData(null, "none")]
        public void Token_error_code_is_a_short_code_or_nothing(string? body, string expected)
        {
            TokenErrorBody.Code(body).Should().Be(expected);
        }

        // ----- Notifier: the "secret notification" pipeline logged the whole request ----------------

        [Fact]
        public async Task Notifier_logs_the_request_shape_never_its_values()
        {
            var logger = new CapturingLogger<NotifierController>();
            var service = new Mock<INotificationService>();
            service.Setup(s => s.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
            var controller = new NotifierController(service.Object, logger);
            var request = new NotifyRequest
            {
                ConfigurationName = "cfg",
                ResponseKey = "otp",
                ResponseValue = Secret,
                DenormalizedPayload = $"{{\"code\":\"{Secret}\"}}",
                UserIds = ["user-1"],
            };

            await controller.SendSecretNotification(request);
            await controller.Notify(request);

            logger.All.Should().NotContain(Secret).And.Contain("cfg");
        }

        // ----- Firebase: logging the response object logged the request's "key=<server key>" ---------

        private sealed class OkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}"), RequestMessage = request });
        }

        [Fact]
        public async Task Firebase_never_logs_the_server_key_or_the_payload()
        {
            var logger = new CapturingLogger<FirebaseNotificationServiceProvider>();
            var repository = new Mock<INotificationRepository>();
            repository.Setup(r => r.GetItemAsync(It.IsAny<Expression<Func<FirebaseConfiguration, bool>>>(), It.IsAny<string>()))
                      .ReturnsAsync(new FirebaseConfiguration { AuthorizationKey = "server-key-" + Secret });
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FirebaseUri"] = "https://firebase.test/send" })
                .Build();
            using var http = new HttpClient(new OkHandler());
            var sut = new FirebaseNotificationServiceProvider(logger, repository.Object, configuration, http);

            await sut.Notify(
                new NotifyRequest { ConfigurationName = "cfg", UserIds = ["user-1"], DenormalizedPayload = $"{{\"otp\":\"{Secret}\"}}" },
                new NotificationConfiguration { Name = "cfg", ChannelToNotify = NotifierTypes.Firebase });

            logger.Lines.Should().NotBeEmpty();
            logger.All.Should().NotContain(Secret);
        }

        // ----- WorkflowService.CreateAsync logged the whole entity, node ClientSecret included --------

        [Fact]
        public async Task Creating_a_workflow_never_logs_its_node_parameters()
        {
            TestBlocksContext.Set("tenant-wf", "user-wf");
            try
            {
                var logger = new CapturingLogger<WorkflowService>();
                var sut = new WorkflowService(Mock.Of<IWorkflowRepository>(), Mock.Of<IWorkflowVersionRepository>(),
                    Mock.Of<IScheduleService>(), logger);
                // Create cannot read object-valued parameters through Newtonsoft today, so the marker rides in
                // a node field instead: the point is that no node content reaches the log at all.
                var nodes = JsonDocument.Parse(
                    "[{\"id\":\"n1\",\"name\":\"" + Secret + "\",\"type\":\"httpRequest\",\"version\":\"1\"," +
                    "\"category\":\"action\",\"position\":{\"x\":0,\"y\":0}}]").RootElement;

                var response = await sut.CreateAsync("tenant-wf", new WorkflowCreateRequestDto { Name = "wf", Nodes = nodes });

                response.IsSuccess.Should().BeTrue();
                logger.All.Should().NotContain(Secret).And.Contain("1 node(s)");
            }
            finally
            {
                TestBlocksContext.Clear();
            }
        }

        // ----- Schedule webhook: a URL can carry its secret in the path or query ----------------------

        [Theory]
        [InlineData("https://hooks.slack.com/services/T000/B000/" + Secret, "hooks.slack.com")]
        [InlineData("https://api.example.test/hook?token=" + Secret, "api.example.test")]
        [InlineData("not a url " + Secret, "(invalid url)")]
        [InlineData(null, "(invalid url)")]
        public void Webhook_logs_name_the_host_only(string? url, string expected)
        {
            SchedulePublisherService.WebhookHost(url).Should().Be(expected);
        }

        // ----- Code node: console.* used to print tenant data to the server's stdout -----------------

        [Fact]
        public async Task Code_node_console_output_never_reaches_the_server_stdout()
        {
            var item = new WorkflowItemExecutionEntity
            {
                Id = "a", WorkflowExecutionId = "exec-1", TenantId = "tenant-1", NodeId = "n0", NodeExecutionId = "ne-0",
                NodeName = "Node1", Branch = "source", ParentItemIds = new List<string>(),
                AncestorMap = new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = new BsonDocument { { "token", Secret } } },
            };
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "Mode", "all" },
                    { "Language", "js" },
                    { "Script", "console.log($items[0].json.token); console.warn($items[0].json.token); console.error($items[0].json.token); return [{ ok: true }];" },
                },
                InputItems = new List<WorkflowItemExecutionEntity> { item },
                IterationCount = 1,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var captured = new StringWriter();
            NodeExecutionResult result;
            try
            {
                Console.SetOut(captured);
                Console.SetError(captured);
                result = await new TransformCodeV1Node().RunAsync(context);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            captured.ToString().Should().NotContain(Secret);
        }
    }
}
