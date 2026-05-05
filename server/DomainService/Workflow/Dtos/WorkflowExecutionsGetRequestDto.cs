using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowExecutionsGetRequestDto : IProjectKey
    {
        public required string ProjectKey { get; set; }
        public required string WorkflowId { get; set; }
    }
}
