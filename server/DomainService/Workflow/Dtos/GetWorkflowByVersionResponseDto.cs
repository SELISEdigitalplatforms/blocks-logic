using Blocks.Genesis;
namespace DomainService.Workflow.Dtos
{
    public class GetWorkflowByVersionResponseDto : BaseResponse
    {
        public WorkflowResponseDto data { get; set; }
    }
}