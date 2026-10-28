using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using DomainService.Configuration.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionNotification;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Send Notification step: picks the recipient fields by the configuration's receiver type, fills
    /// expressions per item, refuses what a workflow must not send, and turns a refused send into an error item.
    /// The Notifier itself is behind <see cref="INotificationNodeSender"/>.
    /// </summary>
    public class ActionNotificationNodeTests
    {
        private const string Tenant = "tenant-1";

        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = Tenant,
            NodeId = "node-1",
            NodeExecutionId = "ne-1",
            NodeName = "Node1",
            Branch = "source",
            ItemIndex = 0,
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output },
        };

        private static NodeExecutionContext Context(BsonDocument parameters, params BsonDocument[] outputs)
        {
            var items = outputs.Select((o, i) => Item("item-" + i, o)).ToList();
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = Tenant,
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
            };
        }

        private static BsonDocument Parameters(string configuration = "order-shipped", string payload = "{ \"title\": \"Order {{$json.id}}\" }") => new()
        {
            { "ConfigurationName", configuration },
            { "UserIds", new BsonArray { "{{$json.userId}}" } },
            { "Roles", new BsonArray() },
            { "OrganizationIds", new BsonArray() },
            { "SubscriptionFilters", new BsonArray { new BsonDocument { { "Context", "order" }, { "ActionName", "status" }, { "Value", "{{$json.id}}" } } } },
            { "Payload", payload },
            { "SaveAsObject", false },
            { "ResponseKey", "" },
            { "ResponseValue", "" },
        };

        private static NotificationConfiguration Config(
            NotificationReceiverTypes type = NotificationReceiverTypes.UserSpecificReceiverType,
            bool builtIn = false,
            bool disabled = false) => new()
            {
                Name = "order-shipped",
                NotificationType = type,
                ChannelToNotify = NotifierTypes.SignalR,
                NotifyMethod = "OrderShipped",
                IsBuiltIn = builtIn,
                IsDisabled = disabled,
            };

        private sealed class Harness
        {
            public Mock<IConfigurationRepository> Configurations { get; } = new();
            public Mock<INotificationNodeSender> Sender { get; } = new();
            public List<NotificationNodeRequest> Sent { get; } = new();

            public Harness(NotificationConfiguration? configuration, NotificationNodeSendResult? answer = null)
            {
                Configurations.Setup(c => c.GetByNameAsync(It.IsAny<string>())).ReturnsAsync(configuration!);
                Sender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<NotificationNodeRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<string, NotificationNodeRequest, CancellationToken>((_, r, _) => Sent.Add(r))
                    .ReturnsAsync(answer ?? new NotificationNodeSendResult(true, null));
            }

            public ActionNotificationNode Node => new(Configurations.Object, Sender.Object);
        }

        private static BsonDocument OutputOf(NodeExecutionResult result, int index = 0) =>
            result.OutputItems[index].Data.Output.AsBsonDocument;

        [Fact]
        public void Node_is_notification_v2_only()
        {
            var node = new Harness(Config()).Node;
            node.NodeType.Should().Be("notification");
            ((INodeExecutor)node).Versions.Should().Equal("v2");
        }

        [Fact]
        public async Task Users_receiver_sends_the_filled_user_ids_and_payload_but_no_filters()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "id", 7 }, { "userId", "u-1" } }));

            result.IsSuccess.Should().BeTrue();
            var request = harness.Sent.Should().ContainSingle().Subject;
            request.ConfigurationName.Should().Be("order-shipped");
            request.UserIds.Should().Equal("u-1");
            request.SubscriptionFilters.Should().BeEmpty();
            JsonDocument.Parse(request.DenormalizedPayload).RootElement.GetProperty("title").GetString().Should().Be("Order 7");
            request.ResponseKey.Should().Be("order-shipped");
            request.ResponseValue.Should().Be("Sent");
            harness.Sender.Verify(s => s.SendAsync(Tenant, It.IsAny<NotificationNodeRequest>(), It.IsAny<CancellationToken>()));

            var output = OutputOf(result);
            output["Success"].AsBoolean.Should().BeTrue();
            output["NotificationType"].AsString.Should().Be("UserSpecificReceiverType");
            output["Recipients"]["UserIds"].AsInt32.Should().Be(1);
        }

        [Fact]
        public async Task Filter_receiver_sends_filled_filters_and_no_users()
        {
            var harness = new Harness(Config(NotificationReceiverTypes.FilterSpecificReceiverType));

            await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "id", "o-9" }, { "userId", "u-1" } }));

            var request = harness.Sent.Should().ContainSingle().Subject;
            request.UserIds.Should().BeEmpty();
            var filter = request.SubscriptionFilters.Should().ContainSingle().Subject;
            filter.Context.Should().Be("order");
            filter.ActionName.Should().Be("status");
            filter.Value.Should().Be("o-9");
        }

        [Fact]
        public async Task Broadcast_receiver_sends_no_recipients()
        {
            var harness = new Harness(Config(NotificationReceiverTypes.BroadcastReceiverType));

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "userId", "u-1" } }));

            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeFalse();
            var request = harness.Sent.Should().ContainSingle().Subject;
            request.UserIds.Should().BeEmpty();
            request.SubscriptionFilters.Should().BeEmpty();
        }

        [Fact]
        public async Task A_user_id_expression_that_fills_to_an_array_adds_each_id_once()
        {
            var harness = new Harness(Config());
            var parameters = Parameters();
            parameters["UserIds"] = new BsonArray { "{{$json.userIds}}", "u-2", "" };

            await harness.Node.RunAsync(Context(parameters, new BsonDocument { { "userIds", new BsonArray { "u-1", "u-2" } } }));

            harness.Sent.Should().ContainSingle().Which.UserIds.Should().Equal("u-1", "u-2");
        }

        [Fact]
        public async Task One_notification_is_sent_per_item()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(),
                new BsonDocument { { "userId", "u-1" } }, new BsonDocument { { "userId", "u-2" } }));

            result.OutputItems.Should().HaveCount(2);
            harness.Sent.Select(r => r.UserIds.Single()).Should().Equal("u-1", "u-2");
        }

        [Fact]
        public async Task Users_receiver_without_any_recipient_is_an_error_item_and_nothing_is_sent()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument()));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeTrue();
            OutputOf(result)["message"].AsString.Should().StartWith("No recipient");
            harness.Sent.Should().BeEmpty();
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("builtIn")]
        [InlineData("noReceiver")]
        public async Task Configurations_a_workflow_cannot_use_are_error_items(string kind)
        {
            var configuration = kind switch
            {
                "missing" => null,
                "builtIn" => Config(builtIn: true),
                _ => Config(NotificationReceiverTypes.NoReceiverType),
            };
            var harness = new Harness(configuration);

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "userId", "u-1" } }));

            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeTrue();
            harness.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task A_configuration_disabled_by_the_tenant_is_skipped_not_sent()
        {
            var harness = new Harness(Config(disabled: true));

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "userId", "u-1" } }));

            harness.Sent.Should().BeEmpty();
            var output = OutputOf(result);
            output["Success"].AsBoolean.Should().BeTrue();
            output["Skipped"].AsString.Should().Be("disabled");
        }

        [Fact]
        public async Task A_refused_send_is_an_error_item_with_the_notifier_reason()
        {
            var harness = new Harness(Config(), new NotificationNodeSendResult(false, "The Notifier answered HTTP 401."));

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "userId", "u-1" } }));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeTrue();
            OutputOf(result)["message"].AsString.Should().Contain("HTTP 401");
        }

        [Fact]
        public async Task A_payload_that_is_not_json_after_filling_fails_the_step()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(payload: "{ \"title\": }"), new BsonDocument { { "userId", "u-1" } }));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Payload is not valid JSON");
            harness.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task A_payload_over_the_limit_is_an_error_item()
        {
            var harness = new Harness(Config());
            var big = new string('x', ActionNotificationNode.MaxPayloadBytes);

            var result = await harness.Node.RunAsync(Context(Parameters(payload: $"{{ \"text\": \"{big}\" }}"), new BsonDocument { { "userId", "u-1" } }));

            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeTrue();
            harness.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task An_empty_payload_is_sent_as_an_empty_object()
        {
            var harness = new Harness(Config());

            await harness.Node.RunAsync(Context(Parameters(payload: ""), new BsonDocument { { "userId", "u-1" } }));

            harness.Sent.Should().ContainSingle().Which.DenormalizedPayload.Should().Be("{}");
        }

        [Fact]
        public async Task The_configuration_is_looked_up_once_per_step()
        {
            var harness = new Harness(Config());

            await harness.Node.RunAsync(Context(Parameters(),
                new BsonDocument { { "userId", "u-1" } }, new BsonDocument { { "userId", "u-2" } }));

            harness.Configurations.Verify(c => c.GetByNameAsync("order-shipped"), Times.Once);
        }
    }

    /// <summary>The Notifier call: project-signed request, and every kind of answer read without throwing.</summary>
    public class NotificationNodeSenderTests
    {
        private const string Url = "https://notify.example.com/api/Notifier/Notify";
        private const string Tenant = "tenant-1";
        private const string SecretValue = "hashed";

        private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
                return await respond(request, cancellationToken);
            }
        }

        private static (NotificationNodeSender Sender, StubHandler Handler, Mock<ICryptoService> Crypto) Build(
            HttpStatusCode status = HttpStatusCode.OK, string body = "{\"isSuccess\":true}", string? url = Url, bool knownTenant = true)
        {
            var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

            var settings = new Dictionary<string, string?>();
            if (url is not null) settings["NotificationServiceUrl"] = url;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var crypto = new Mock<ICryptoService>();
            crypto.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>())).Returns(SecretValue);

            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(Tenant)).Returns(knownTenant
                ? new Tenant
                {
                    TenantId = Tenant,
                    TenantSalt = "salt",
                    DbConnectionString = "conn",
                    JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "x", IssueDate = DateTime.UtcNow },
                }
                : null);

            return (new NotificationNodeSender(configuration, tenants.Object, crypto.Object, factory.Object), handler, crypto);
        }

        private static NotificationNodeRequest Request() => new()
        {
            ConfigurationName = "order-shipped",
            UserIds = ["u-1"],
            DenormalizedPayload = "{\"title\":\"Hi\"}",
            ResponseKey = "order-shipped",
            ResponseValue = "Sent",
        };

        [Fact]
        public async Task Posts_the_request_signed_with_the_project_key()
        {
            var (sender, handler, crypto) = Build();

            var result = await sender.SendAsync(Tenant, Request(), CancellationToken.None);

            result.Success.Should().BeTrue();
            var (request, body) = handler.Calls.Should().ContainSingle().Subject;
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be(Url);
            request.Headers.GetValues("x-blocks-key").Should().Equal(Tenant);
            request.Headers.GetValues("Secret").Should().Equal(SecretValue);
            crypto.Verify(c => c.Hash(Tenant, "salt", It.IsAny<bool>()));

            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("configurationName").GetString().Should().Be("order-shipped");
            json.RootElement.GetProperty("userIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("u-1");
            json.RootElement.GetProperty("denormalizedPayload").GetString().Should().Be("{\"title\":\"Hi\"}");
            json.RootElement.GetProperty("contentAvailable").GetBoolean().Should().BeTrue();
        }

        [Fact]
        public async Task A_notifier_validation_failure_returns_its_errors()
        {
            var (sender, _, _) = Build(body: "{\"isSuccess\":false,\"errors\":{\"ConfigurationName\":\"no_configuration_exist\"}}");

            var result = await sender.SendAsync(Tenant, Request(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Error.Should().Be("ConfigurationName: no_configuration_exist");
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "", "HTTP 401")]
        [InlineData(HttpStatusCode.OK, "not json", "could not be read")]
        public async Task Other_answers_are_failures_with_a_reason(HttpStatusCode status, string body, string reason)
        {
            var (sender, _, _) = Build(status, body);

            var result = await sender.SendAsync(Tenant, Request(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain(reason);
        }

        [Fact]
        public async Task Nothing_is_sent_without_a_url_or_a_known_project()
        {
            var (noUrl, noUrlHandler, _) = Build(url: null);
            (await noUrl.SendAsync(Tenant, Request(), CancellationToken.None)).Success.Should().BeFalse();
            noUrlHandler.Calls.Should().BeEmpty();

            var (unknown, unknownHandler, _) = Build(knownTenant: false);
            (await unknown.SendAsync(Tenant, Request(), CancellationToken.None)).Success.Should().BeFalse();
            unknownHandler.Calls.Should().BeEmpty();
        }
    }
}
