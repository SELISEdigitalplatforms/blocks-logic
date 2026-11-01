using Blocks.Genesis.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationData = global::Workflow.DomainService.Dtos.NotificationData;
using WorkflowNotificationService = global::Workflow.DomainService.Services.WorkflowNotificationService;

using Workflow.DomainService.Utils;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// WorkflowNotificationService publishes workflow test streams and import results through the genesis publisher,
    /// in the ROOT tenant as the old HTTP send with the root key did: the receivers are the console users who run the
    /// test, and pages match the result by ResponseKey/ResponseValue.
    /// </summary>
    public class WorkflowNotificationServiceTests
    {
        private sealed class FakePublisher : INotificationPublisher
        {
            public List<NotificationRequest> Requests { get; } = [];

            public void Publish(NotificationRequest request) => Requests.Add(request);
        }

        private static (WorkflowNotificationService Sut, FakePublisher Publisher) Build(string? configurationName = "workflow-config")
        {
            var settings = new Dictionary<string, string?> { ["RootTenantId"] = "root-tenant" };
            if (configurationName is not null)
                settings["WORKFLOW_NOTIFICATION_CONFIGURATION_NAME"] = configurationName;
            var publisher = new FakePublisher();
            var sut = new WorkflowNotificationService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), publisher,
                NullLogger<WorkflowNotificationService>.Instance);
            return (sut, publisher);
        }

        private static NotificationData Data() => new()
        {
            Title = "Workflow Execution",
            Description = "done",
            ResponseKey = "WorkflowExecution",
            ResponseValue = "Sent",
            Information = new Dictionary<string, object> { ["workflowId"] = "wf-1" },
        };

        [Fact]
        public async Task Notify_PublishesInTheRootTenantToTheTestUsers_WithTheResponseKey()
        {
            var (sut, publisher) = Build();

            (await sut.Notify(["user-1"], Data())).Should().BeTrue();

            var request = publisher.Requests.Single();
            request.Name.Should().Be("workflow-config");
            request.TenantId.Should().Be("root-tenant");
            request.NamedUserIds.Should().Equal("user-1");
            request.ResponseKey.Should().Be("WorkflowExecution");
            request.ResponseValue.Should().Be("Sent");
            ((string)request.Payload!).Should().Contain("wf-1").And.Contain("Workflow Execution");
        }

        [Fact]
        public async Task Notify_WithoutAConfigurationName_PublishesNothing()
        {
            var (sut, publisher) = Build(configurationName: null);

            (await sut.Notify(["user-1"], Data())).Should().BeFalse();

            publisher.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task NotifyImportAsync_UsesTheWorkflowConfigurationAndCorrelationId()
        {
            var (sut, publisher) = Build();

            await sut.NotifyImportAsync(["user-1"], "cor-9", isSuccess: true, title: "Workflow imported",
                description: "Your workflow is ready.", workflowId: "wf-imported", issues: 0);

            // The configuration execution events use (its event the console listens to), not the never-created "workflow-import".
            var request = publisher.Requests.Single();
            request.Name.Should().Be("workflow-config").And.NotBe(LogicConstants.WorkflowImportNotificationConfigurationName);
            request.ResponseKey.Should().Be("cor-9");
            ((string)request.Payload!).Should().Contain("wf-imported").And.Contain("Workflow imported");
        }
    }
}
