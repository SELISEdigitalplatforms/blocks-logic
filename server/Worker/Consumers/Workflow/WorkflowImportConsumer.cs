using Blocks.Genesis;
using Workflow.DomainService.Events;
using Workflow.DomainService.Services;

namespace Worker.Consumers.Workflow
{
    public class WorkflowImportConsumer : IConsumer<WorkflowImportEvent>
    {
        private readonly IWorkflowImportService _workflowImportService;
        private readonly ILogger<WorkflowImportConsumer> _logger;

        public WorkflowImportConsumer(
            IWorkflowImportService workflowImportService,
            ILogger<WorkflowImportConsumer> logger)
        {
            _workflowImportService = workflowImportService;
            _logger = logger;
        }

        public async Task Consume(WorkflowImportEvent @event)
        {
            var previous = BlocksContext.GetContext();
            try
            {
                var tenantId = @event.TenantId ?? string.Empty;
                var userId = @event.UserId ?? string.Empty;
                BlocksContext.SetContext(
                    BlocksContext.Create(
                        tenantId: tenantId,
                        roles: [],
                        userId: userId,
                        isAuthenticated: !string.IsNullOrEmpty(userId),
                        requestUri: string.Empty,
                        organizationId: string.Empty,
                        expireOn: DateTime.MinValue,
                        email: string.Empty,
                        permissions: [],
                        userName: userId,
                        phoneNumber: string.Empty,
                        displayName: userId,
                        oauthToken: string.Empty,
                        originalTenantId: tenantId,
                        applicationDomain: string.Empty),
                    false);

                await _workflowImportService.ImportAsync(@event);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WorkflowImportConsumer failed for FileId {FileId}", @event.FileId);
                throw;
            }
            finally
            {
                BlocksContext.SetContext(previous, previous is not null);
            }
        }
    }
}
