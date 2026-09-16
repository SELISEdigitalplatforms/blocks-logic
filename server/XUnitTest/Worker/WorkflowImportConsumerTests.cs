using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Events;
using Workflow.DomainService.Services;
using Worker.Consumers.Workflow;
using XUnitTest.TestHelpers;

namespace XUnitTest.Worker
{
    public class WorkflowImportConsumerTests : IDisposable
    {
        private readonly Mock<IWorkflowImportService> _importService = new();
        private readonly WorkflowImportConsumer _consumer;

        public WorkflowImportConsumerTests()
        {
            TestBlocksContext.Set("previous-tenant", "previous-user");
            _consumer = new WorkflowImportConsumer(
                _importService.Object,
                NullLogger<WorkflowImportConsumer>.Instance);
        }

        public void Dispose() => TestBlocksContext.Clear();

        [Fact]
        public async Task Consume_SetsTenantContextAndDelegates()
        {
            WorkflowImportEvent? captured = null;
            string? tenantDuringImport = null;
            _importService.Setup(s => s.ImportAsync(It.IsAny<WorkflowImportEvent>()))
                .Callback<WorkflowImportEvent>(evt =>
                {
                    captured = evt;
                    tenantDuringImport = BlocksContext.GetContext()?.TenantId;
                })
                .Returns(Task.CompletedTask);

            var evt = new WorkflowImportEvent
            {
                FileId = "file-1",
                MessageCoRelationId = "cor-1",
                TenantId = "dest-tenant",
                UserId = "dest-user",
            };

            await _consumer.Consume(evt);

            captured.Should().BeSameAs(evt);
            tenantDuringImport.Should().Be("dest-tenant");
            BlocksContext.GetContext()!.TenantId.Should().Be("previous-tenant");
        }
    }
}
