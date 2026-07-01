
namespace DomainService.Workflow.Dtos
{
    public class LastSuccessfullExecutionRequestDto
    {
        public required string ProjectKey { get; set; }
        public required string WorkflowId { get; set; }
    }
}
