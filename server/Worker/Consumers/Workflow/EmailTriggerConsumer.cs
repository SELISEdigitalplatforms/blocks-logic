using Blocks.Genesis;
using Mail.DomainService.Mails;
using DomainService.Workflow.Services;

namespace Worker.Consumers.Workflow
{
    public class EmailTriggerConsumer : IConsumer<EmailTriggerEvent>
    {
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public EmailTriggerConsumer(IWorkflowExecutionService workflowExecutionService)
        {
            _workflowExecutionService = workflowExecutionService;
        }

        public async Task Consume(EmailTriggerEvent @event)
        {
            await _workflowExecutionService.EmailTriggerStartAsync(@event);
        }
    }
}
