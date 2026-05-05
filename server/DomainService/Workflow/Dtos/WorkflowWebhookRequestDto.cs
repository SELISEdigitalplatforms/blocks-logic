using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowWebhookRequestDto : IProjectKey
    {
        public required string ProjectKey { get; set; }
        public object Input { get; set; }
    }
}

