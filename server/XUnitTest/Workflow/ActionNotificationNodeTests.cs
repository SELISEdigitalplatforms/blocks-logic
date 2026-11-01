using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using Blocks.Genesis.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionNotification;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Send Notification step: fills expressions per item, refuses what the configuration cannot send (by its
    /// audience, scope and channels), and publishes through genesis <see cref="INotificationPublisher"/>.
    /// </summary>
    public class ActionNotificationNodeTests
    {
        private const string Tenant = "tenant-1";
        private const string Name = "order-shipped";

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

        private static BsonDocument Parameters(
            string configuration = Name,
            string payload = "{ \"title\": \"Order {{$json.id}}\" }",
            BsonArray? userIds = null,
            string organizationId = "") => new()
        {
            { "ConfigurationName", configuration },
            { "UserIds", userIds ?? new BsonArray { "{{$json.userId}}" } },
            { "OrganizationId", organizationId },
            { "Payload", payload },
            { "SubscriptionFilters", new BsonArray { new BsonDocument { { "Context", "order" }, { "ActionName", "status" }, { "Value", "{{$json.id}}" } } } },
            { "ResponseKey", "" },
            { "ResponseValue", "" },
        };

        private static NotificationConfiguration Config(
            NotificationAudienceLevel level = NotificationAudienceLevel.AffectedOnly,
            NotificationScope scope = NotificationScope.App,
            bool live = true,
            bool push = false,
            bool inbox = true,
            bool builtIn = false,
            bool disabled = false,
            int rateLimit = 0) => new()
            {
                Name = Name,
                NotifyMethod = Name,
                Scope = scope,
                Audience = new NotificationAudience { Level = level },
                LiveEnabled = live,
                PushEnabled = push,
                EnablePersistence = inbox,
                IsBuiltIn = builtIn,
                IsDisabled = disabled,
                RateLimitPerMinute = rateLimit,
            };

        private sealed class Harness
        {
            public Mock<IKeyValueStore> Store { get; } = new();
            public Mock<INotificationPublisher> Publisher { get; } = new();
            public List<NotificationRequest> Published { get; } = new();

            public Harness(NotificationConfiguration? configuration)
            {
                Store.Setup(s => s.GetAsync<NotificationConfiguration>(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(configuration);
                Publisher.Setup(p => p.Publish(It.IsAny<NotificationRequest>()))
                    .Callback<NotificationRequest>(Published.Add);
            }

            public ActionNotificationNode Node => new(Store.Object, Publisher.Object);
        }

        private static BsonDocument OutputOf(NodeExecutionResult result, int index = 0) =>
            result.OutputItems[index].Data.Output.AsBsonDocument;

        private static string ErrorOf(NodeExecutionResult result, int index = 0) =>
            result.OutputItems[index].Data.Output["message"].AsString;

        [Fact]
        public void Node_is_notification_v2_only()
        {
            var node = new Harness(Config()).Node;
            node.NodeType.Should().Be("notification");
            ((INodeExecutor)node).Versions.Should().Equal("v2");
        }

        [Fact]
        public async Task Publishes_the_filled_request_for_the_project()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "id", 7 }, { "userId", "u-1" } }));

            result.IsSuccess.Should().BeTrue();
            var request = harness.Published.Should().ContainSingle().Subject;
            request.Name.Should().Be(Name);
            request.TenantId.Should().Be(Tenant);
            request.Id.Should().NotBe(Guid.Empty);
            request.NamedUserIds.Should().Equal("u-1");
            request.OrganizationId.Should().Be("default");
            JsonDocument.Parse((string)request.Payload!).RootElement.GetProperty("title").GetString().Should().Be("Order 7");
            var filter = request.SubscriptionFilters.Should().ContainSingle().Subject;
            filter.Context.Should().Be("order");
            filter.Value.Should().Be("7");
            request.ResponseKey.Should().Be(Name);
            request.ResponseValue.Should().Be("Sent");

            harness.Store.Verify(s => s.GetAsync<NotificationConfiguration>("notification-config:" + Name, true, It.IsAny<CancellationToken>()));

            var output = OutputOf(result);
            output["Queued"].AsBoolean.Should().BeTrue();
            output["NotificationId"].AsString.Should().Be(request.Id.ToString());
            output["Recipients"].AsString.Should().Be("AffectedOnly");
            output["Scope"].AsString.Should().Be("App");
            output["Channels"]["InApp"].AsBoolean.Should().BeTrue();
            output["Channels"]["Offline"].AsBoolean.Should().BeTrue();
            output["NamedUsers"].AsInt32.Should().Be(1);
            output.Contains("Skipped").Should().BeFalse();
        }

        [Fact]
        public async Task A_user_id_expression_that_fills_to_an_array_adds_each_user_once()
        {
            var harness = new Harness(Config());

            await harness.Node.RunAsync(Context(
                Parameters(userIds: new BsonArray { "{{$json.ids}}", "u-1", " " }),
                new BsonDocument { { "ids", new BsonArray { "u-1", "u-2" } } }));

            harness.Published.Single().NamedUserIds.Should().Equal("u-1", "u-2");
        }

        [Fact]
        public async Task Organization_and_response_fields_are_filled_when_set()
        {
            var harness = new Harness(Config(NotificationAudienceLevel.AllUsers));
            var parameters = Parameters(organizationId: "{{$json.org}}", userIds: new BsonArray());
            parameters["ResponseKey"] = "order";
            parameters["ResponseValue"] = "{{$json.id}}";

            await harness.Node.RunAsync(Context(parameters, new BsonDocument { { "id", 3 }, { "org", "org-9" } }));

            var request = harness.Published.Single();
            request.OrganizationId.Should().Be("org-9");
            request.ResponseKey.Should().Be("order");
            request.ResponseValue.Should().Be("3");
        }

        [Fact]
        public async Task A_payload_that_is_not_json_after_filling_fails_the_step()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(payload: "{ \"title\": }"), new BsonDocument { { "userId", "u-1" } }));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Payload is not valid JSON");
            harness.Published.Should().BeEmpty();
        }

        [Fact]
        public async Task A_payload_over_the_limit_is_an_error_item()
        {
            var harness = new Harness(Config());
            var big = new string('x', ActionNotificationNode.MaxPayloadBytes);

            var result = await harness.Node.RunAsync(Context(
                Parameters(payload: "{ \"text\": \"" + big + "\" }"),
                new BsonDocument { { "userId", "u-1" } }));

            result.IsSuccess.Should().BeTrue();
            ErrorOf(result).Should().Contain("larger than 64 KB");
            harness.Published.Should().BeEmpty();
        }

        public static TheoryData<NotificationConfiguration?, bool, string> Refusals() => new()
        {
            { null, true, "No notification configuration named 'order-shipped' in this project." },
            { Config(builtIn: true), true, "'order-shipped' is a built-in notification configuration; a workflow cannot send it." },
            { Config(live: false, push: false, inbox: false), true, "'order-shipped' has no delivery channel on (in-app, mobile push or offline)." },
            { Config(NotificationAudienceLevel.AffectedOnly), false, "'order-shipped' sends only to the users the event is about: set at least one user id." },
            { Config(NotificationAudienceLevel.AllUsers, NotificationScope.Console), false, "'order-shipped' goes to console users, who must be named: set at least one user id." },
        };

        [Theory]
        [MemberData(nameof(Refusals))]
        public async Task A_configuration_that_cannot_be_sent_is_an_error_item(NotificationConfiguration? configuration, bool withUser, string message)
        {
            var harness = new Harness(configuration);
            var userIds = withUser ? new BsonArray { "u-1" } : new BsonArray();

            var result = await harness.Node.RunAsync(Context(Parameters(userIds: userIds), new BsonDocument()));

            result.IsSuccess.Should().BeTrue();
            ErrorOf(result).Should().Be(message);
            harness.Published.Should().BeEmpty();
        }

        [Theory]
        [InlineData(NotificationAudienceLevel.AllUsers)]
        [InlineData(NotificationAudienceLevel.Selected)]
        public async Task All_users_and_targeted_need_no_named_user(NotificationAudienceLevel level)
        {
            var harness = new Harness(Config(level));

            var result = await harness.Node.RunAsync(Context(Parameters(userIds: new BsonArray()), new BsonDocument()));

            OutputOf(result)["Queued"].AsBoolean.Should().BeTrue();
            harness.Published.Single().NamedUserIds.Should().BeEmpty();
        }

        [Fact]
        public async Task A_blank_configuration_name_is_an_error_item()
        {
            var harness = new Harness(Config());

            var result = await harness.Node.RunAsync(Context(Parameters(configuration: " "), new BsonDocument { { "userId", "u-1" } }));

            ErrorOf(result).Should().Be("No notification configuration is selected.");
            harness.Published.Should().BeEmpty();
        }

        [Fact]
        public async Task A_disabled_configuration_is_skipped_not_published()
        {
            var harness = new Harness(Config(disabled: true));

            var result = await harness.Node.RunAsync(Context(Parameters(), new BsonDocument { { "userId", "u-1" } }));

            var output = OutputOf(result);
            output["Queued"].AsBoolean.Should().BeFalse();
            output["Skipped"].AsString.Should().Be("disabled");
            harness.Published.Should().BeEmpty();
        }

        [Fact]
        public async Task The_configuration_is_read_once_per_step()
        {
            var harness = new Harness(Config());

            await harness.Node.RunAsync(Context(Parameters(),
                new BsonDocument { { "userId", "u-1" } },
                new BsonDocument { { "userId", "u-2" } },
                new BsonDocument { { "userId", "u-3" } }));

            harness.Published.Should().HaveCount(3);
            harness.Store.Verify(s => s.GetAsync<NotificationConfiguration>(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>Keeps each rendered stage line.</summary>
        private sealed class ListLogger : ILogger<WorkflowExecutionLogger>
        {
            public List<string> Lines { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Lines.Add(formatter(state, exception));
        }

        [Fact]
        public async Task The_step_log_has_counts_only_and_warns_over_the_rate_limit()
        {
            var harness = new Harness(Config(rateLimit: 1));
            var logger = new ListLogger();
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = Tenant,
                WorkflowId = "wf-1",
                WorkflowName = "Orders",
                WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "Orders", TenantId = Tenant },
                TriggerMetadata = new TriggerMetadata(),
                TraceId = "0af7651916cd43dd8448eb211c80319c",
            };
            var context = Context(Parameters(),
                new BsonDocument { { "userId", "secret-user-1" }, { "id", 1 } },
                new BsonDocument { { "userId", "secret-user-2" }, { "id", 2 } });
            context.Log = new WorkflowExecutionLogger(logger).For(execution).ForNode("node-1", 1);

            await harness.Node.RunAsync(context);

            logger.Lines.Should().Contain(l => l.Contains("[wf:node.notification.finished]") && l.Contains("2 of 2"));
            logger.Lines.Should().Contain(l => l.Contains("[wf:node.notification.rateLimit]"));
            logger.Lines.Should().NotContain(l => l.Contains("secret-user") || l.Contains(Name) || l.Contains("Order"));
        }

        [Theory]
        [InlineData("{\"ConfigurationName\":\"order-shipped\"}", true)]
        [InlineData("{\"ConfigurationName\":\"\"}", false)]
        [InlineData("{}", false)]
        public async Task ValidateConfiguration_needs_a_name(string json, bool expected)
        {
            using var document = JsonDocument.Parse(json);
            (await ActionNotificationNode.ValidateConfigurationAsync(document)).Should().Be(expected);
        }
    }
}
