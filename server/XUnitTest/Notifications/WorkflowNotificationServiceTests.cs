using System.Net;
using System.Text;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using INotificationService = global::DomainService.Notification.INotificationService;
using NotificationData = global::DomainService.Workflow.Dtos.NotificationData;
using WorkflowNotificationService = global::DomainService.Workflow.Services.WorkflowNotificationService;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// Unit tests for WorkflowNotificationService.Notify.
    ///
    /// Success here is deliberately two conditions rather than one: the call has to return 2xx
    /// AND the body has to say isSuccess. The notification service answers 200 with an error
    /// body, so trusting the status code alone would report delivery that never happened. That
    /// pairing is the main thing these tests hold in place.
    ///
    /// Types are aliased with global:: because XUnitTest.DomainService exists and would
    /// otherwise win the namespace lookup for "DomainService".
    /// </summary>
    public class WorkflowNotificationServiceTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            private readonly Exception? _throw;

            public HttpRequestMessage? Request { get; private set; }
            public string? RequestBody { get; private set; }
            public int Calls { get; private set; }

            public StubHandler(HttpStatusCode status, string body, Exception? shouldThrow = null)
            {
                _status = status;
                _body = body;
                _throw = shouldThrow;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                Request = request;
                RequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_throw is not null)
                {
                    throw _throw;
                }

                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                };
            }
        }

        private static NotificationData Data() => new()
        {
            Title = "Workflow finished",
            Description = "All nodes completed",
            ResponseKey = "workflowId",
            ResponseValue = "wf-1",
            Information = new Dictionary<string, object> { ["nodeCount"] = 3 },
        };

        private static (WorkflowNotificationService sut, StubHandler handler) Build(
            HttpStatusCode status = HttpStatusCode.OK,
            string body = """{"isSuccess":true}""",
            Exception? shouldThrow = null,
            string? notificationUrl = "https://notify.example.com/send")
        {
            var handler = new StubHandler(status, body, shouldThrow);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(handler, disposeHandler: false));

            var settings = new Dictionary<string, string?>
            {
                ["RootTenantId"] = "root-tenant",
                ["WORKFLOW_NOTIFICATION_CONFIGURATION_NAME"] = "workflow-config",
            };
            if (notificationUrl is not null)
            {
                settings["NotificationServiceUrl"] = notificationUrl;
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var crypto = new Mock<ICryptoService>();
            crypto.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string>())).Returns("hashed-secret");

            // The service reads the salt through `GetTenantByID(...)?.TenantSalt`, so an absent
            // tenant is a supported path rather than a crash. Returning null exercises it and
            // avoids building a Tenant, whose required members are unrelated to this service.
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            var notifications = new Mock<INotificationService>();

            return (new WorkflowNotificationService(
                crypto.Object,
                tenants.Object,
                configuration,
                notifications.Object,
                factory.Object,
                NullLogger<WorkflowNotificationService>.Instance), handler);
        }

        [Fact]
        public async Task A_missing_notification_url_fails_without_attempting_a_call()
        {
            var (sut, handler) = Build(notificationUrl: null);

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeFalse();
            handler.Calls.Should().Be(0, "there is nowhere to post to");
        }

        [Fact]
        public async Task A_successful_delivery_needs_both_a_2xx_and_an_isSuccess_body()
        {
            var (sut, _) = Build(status: HttpStatusCode.OK, body: """{"isSuccess":true}""");

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeTrue();
        }

        [Fact]
        public async Task A_2xx_carrying_an_unsuccessful_body_is_not_treated_as_delivered()
        {
            // This is the case a status-only check would get wrong.
            var (sut, _) = Build(status: HttpStatusCode.OK, body: """{"isSuccess":false,"errors":"unknown user"}""");

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeFalse();
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task A_non_success_status_fails_even_if_the_body_claims_success(HttpStatusCode status)
        {
            var (sut, _) = Build(status: status, body: """{"isSuccess":true}""");

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeFalse();
        }

        [Fact]
        public async Task An_unparseable_body_fails_rather_than_propagating_a_json_error()
        {
            var (sut, _) = Build(body: "<html>gateway error</html>");

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeFalse();
        }

        [Fact]
        public async Task An_empty_body_is_not_treated_as_success()
        {
            var (sut, _) = Build(body: "");

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeFalse();
        }

        [Fact]
        public async Task A_transport_failure_fails_quietly_rather_than_throwing()
        {
            // Notify is called from workflow execution, so a throw here would fail the whole
            // run over an undelivered notification.
            var (sut, _) = Build(shouldThrow: new HttpRequestException("connection refused"));

            var sent = await sut.Notify(["user-1"], Data());

            sent.Should().BeFalse();
        }

        [Fact]
        public async Task The_request_carries_the_tenant_key_and_the_derived_secret()
        {
            var (sut, handler) = Build();

            await sut.Notify(["user-1"], Data());

            handler.Request!.Headers.GetValues("x-blocks-key").Should().ContainSingle()
                .Which.Should().Be("root-tenant");
            handler.Request.Headers.GetValues("Secret").Should().ContainSingle()
                .Which.Should().Be("hashed-secret");
        }

        [Fact]
        public async Task The_payload_names_the_recipients_and_the_configured_channel()
        {
            var (sut, handler) = Build();

            await sut.Notify(["user-1", "user-2"], Data());

            handler.RequestBody.Should().Contain("user-1").And.Contain("user-2");
            handler.RequestBody.Should().Contain("workflow-config");
        }

        [Fact]
        public async Task The_title_and_description_travel_inside_the_denormalized_payload()
        {
            // The inner object is serialised to a string before the outer one is, so a change
            // to that nesting would not show up as a compile error anywhere.
            var (sut, handler) = Build();

            await sut.Notify(["user-1"], Data());

            handler.RequestBody.Should().Contain("denormalizedPayload");
            handler.RequestBody.Should().Contain("Workflow finished");
            handler.RequestBody.Should().Contain("All nodes completed");
        }

        [Fact]
        public async Task The_response_key_and_value_are_sent_so_the_client_can_correlate()
        {
            var (sut, handler) = Build();

            await sut.Notify(["user-1"], Data());

            handler.RequestBody.Should().Contain("workflowId");
            handler.RequestBody.Should().Contain("wf-1");
        }
    }
}
