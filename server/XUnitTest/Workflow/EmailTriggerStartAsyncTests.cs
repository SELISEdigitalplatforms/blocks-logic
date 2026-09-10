using Blocks.Genesis;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Enums;
using DomainService.Workflow.Events;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Services;
using DomainService.Workflow.Utils;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using Worker.Consumers.Workflow;

namespace XUnitTest.Workflow
{
    public class EmailTriggerStartAsyncTests
    {
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly Mock<ILogger<WorkflowExecutionService>> _logger = new();
        private readonly Mock<IWorkflowEngineService> _engine = new();
        private readonly Mock<IWorkflowVersionRepository> _versions = new();
        private readonly Mock<IWorkflowNotificationService> _notifications = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly WorkflowExecutionService _sut;

        public EmailTriggerStartAsyncTests()
        {
            _executions
                .Setup(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()))
                .ReturnsAsync((WorkflowExecutionEntity e) => e);
            _notifications
                .Setup(n => n.NotifyExecutionEventAsync(
                    It.IsAny<WorkflowExecutionEntity>(),
                    It.IsAny<NodeExecutionEntity?>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            _messages
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()))
                .Returns(Task.CompletedTask);

            _sut = new WorkflowExecutionService(
                _workflows.Object,
                _executions.Object,
                _messages.Object,
                _logger.Object,
                _engine.Object,
                _versions.Object,
                _notifications.Object,
                _auth.Object,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IDelegationGrantFactory>());
        }

        [Fact]
        public async Task EmailTriggerStartAsync_ShouldQueueTestExecution_WhenSubjectMatches()
        {
            var workflow = CreateWorkflow("wf-1", "config-1", testSubject: "  TEST MAIL  ", isPublished: false);
            _workflows
                .Setup(r => r.GetWorkflowsByMailServerConfigurationIdAsync("tenant-1", "config-1"))
                .ReturnsAsync(new List<WorkflowEntity> { workflow });

            await _sut.EmailTriggerStartAsync(CreateEvent(" test mail "));

            _executions.Verify(r => r.CreateAsync(It.Is<WorkflowExecutionEntity>(e =>
                e.ExecutionMode == WorkflowExecutionMode.Test && e.WorkflowId == "wf-1")), Times.Once);
            _messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()), Times.Once);
            _versions.Verify(r => r.GetWorkflowVersionsAsync(It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
        }

        [Fact]
        public async Task EmailTriggerStartAsync_ShouldQueueProductionExecution_WhenPublishedAndSubjectDoesNotMatch()
        {
            var draft = CreateWorkflow("wf-1", "config-1", testSubject: "other", isPublished: true, publishedVersionId: "ver-1");
            var snapshot = CreateWorkflow("wf-1", "config-1", testSubject: "", isPublished: true, publishedVersionId: "ver-1");
            _workflows
                .Setup(r => r.GetWorkflowsByMailServerConfigurationIdAsync("tenant-1", "config-1"))
                .ReturnsAsync(new List<WorkflowEntity> { draft });
            _versions
                .Setup(r => r.GetWorkflowVersionsAsync("tenant-1", It.Is<string[]>(ids => ids.Contains("wf-1"))))
                .ReturnsAsync(new List<WorkflowVersionEntity>
                {
                    new()
                    {
                        ItemId = "ver-1",
                        WorkflowId = "wf-1",
                        TenantId = "tenant-1",
                        Name = "v1",
                        Snapshot = snapshot
                    }
                });

            await _sut.EmailTriggerStartAsync(CreateEvent("real inbound"));

            _executions.Verify(r => r.CreateAsync(It.Is<WorkflowExecutionEntity>(e =>
                e.ExecutionMode == WorkflowExecutionMode.Production && e.WorkflowSnapshot == snapshot)), Times.Once);
        }

        [Fact]
        public async Task EmailTriggerStartAsync_ShouldSkip_WhenUnpublishedAndSubjectDoesNotMatch()
        {
            var workflow = CreateWorkflow("wf-1", "config-1", testSubject: "", isPublished: false);
            _workflows
                .Setup(r => r.GetWorkflowsByMailServerConfigurationIdAsync("tenant-1", "config-1"))
                .ReturnsAsync(new List<WorkflowEntity> { workflow });

            await _sut.EmailTriggerStartAsync(CreateEvent("real inbound"));

            _executions.Verify(r => r.CreateAsync(It.IsAny<WorkflowExecutionEntity>()), Times.Never);
            _messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()), Times.Never);
        }

        [Fact]
        public async Task EmailTriggerStartAsync_ShouldHandleMixedWorkflowsIndependently()
        {
            var testWorkflow = CreateWorkflow("wf-test", "config-1", testSubject: "TEST MAIL", isPublished: false);
            var unpublished = CreateWorkflow("wf-draft", "config-1", testSubject: "", isPublished: false);
            var published = CreateWorkflow("wf-prod", "config-1", testSubject: "", isPublished: true, publishedVersionId: "ver-prod");
            var snapshot = CreateWorkflow("wf-prod", "config-1", testSubject: "", isPublished: true, publishedVersionId: "ver-prod");

            _workflows
                .Setup(r => r.GetWorkflowsByMailServerConfigurationIdAsync("tenant-1", "config-1"))
                .ReturnsAsync(new List<WorkflowEntity> { testWorkflow, unpublished, published });
            _versions
                .Setup(r => r.GetWorkflowVersionsAsync("tenant-1", It.Is<string[]>(ids => ids.Contains("wf-prod"))))
                .ReturnsAsync(new List<WorkflowVersionEntity>
                {
                    new()
                    {
                        ItemId = "ver-prod",
                        WorkflowId = "wf-prod",
                        TenantId = "tenant-1",
                        Name = "v1",
                        Snapshot = snapshot
                    }
                });

            await _sut.EmailTriggerStartAsync(CreateEvent("TEST MAIL"));

            _executions.Verify(r => r.CreateAsync(It.Is<WorkflowExecutionEntity>(e =>
                e.WorkflowId == "wf-test" && e.ExecutionMode == WorkflowExecutionMode.Test)), Times.Once);
            _executions.Verify(r => r.CreateAsync(It.Is<WorkflowExecutionEntity>(e =>
                e.WorkflowId == "wf-prod" && e.ExecutionMode == WorkflowExecutionMode.Production)), Times.Once);
            _executions.Verify(r => r.CreateAsync(It.Is<WorkflowExecutionEntity>(e => e.WorkflowId == "wf-draft")), Times.Never);
        }

        [Fact]
        public void EmailTriggerQueue_ShouldMatchCommunicationConstants()
        {
            LogicConstants.EmailTriggerQueue.Should().Be(CommunicationConstants.EmailTriggerQueueName);
            LogicConstants.EmailTriggerQueue.Should().Be("blocks_workflow_email_trigger_listener");
        }

        [Fact]
        public async Task EmailTriggerConsumer_ShouldForwardEventToService()
        {
            var service = new Mock<IWorkflowExecutionService>();
            var consumer = new EmailTriggerConsumer(service.Object);
            var evt = CreateEvent("hello");

            await consumer.Consume(evt);

            service.Verify(s => s.EmailTriggerStartAsync(evt), Times.Once);
        }

        private static EmailTriggerEvent CreateEvent(string subject)
        {
            return new EmailTriggerEvent
            {
                Type = EmailTriggerType.Inbound,
                ProjectKey = "tenant-1",
                Mail = new MailBoxEntity
                {
                    ItemId = "mail-1",
                    MessageId = "msg-1",
                    MailServerConfigurationId = "config-1",
                    Subject = subject,
                    From = "from@example.com",
                    To = "to@example.com",
                    Body = "body",
                    Status = MailStatus.Received,
                    Date = DateTime.UtcNow,
                    IsInbound = true
                }
            };
        }

        private static WorkflowEntity CreateWorkflow(
            string itemId,
            string mailServerConfigurationId,
            string testSubject,
            bool isPublished,
            string? publishedVersionId = null)
        {
            return new WorkflowEntity
            {
                ItemId = itemId,
                TenantId = "tenant-1",
                Name = itemId,
                IsPublished = isPublished,
                PublishedVersionId = publishedVersionId,
                Nodes =
                [
                    new NodeEntity
                    {
                        Id = $"node-{itemId}",
                        Name = "Email Trigger",
                        Category = "trigger",
                        Type = "email",
                        Version = "v1",
                        Position = new Position(),
                        Parameters = new BsonDocument
                        {
                            { "mailServerConfigurationId", mailServerConfigurationId },
                            { "testSubject", testSubject }
                        }
                    }
                ]
            };
        }
    }
}
