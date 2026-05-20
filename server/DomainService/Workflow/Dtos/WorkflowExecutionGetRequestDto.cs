using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class WorkflowExecutionGetRequestDto : IProjectKey
    {
        public required string ProjectKey { get; set; }
        public required string ExecutionId { get; set; }
    }
}
