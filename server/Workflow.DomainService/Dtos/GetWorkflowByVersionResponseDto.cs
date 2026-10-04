using Blocks.Genesis;
namespace Workflow.DomainService.Dtos
{
    public class GetWorkflowByVersionResponseDto : BaseResponse
    {
        public WorkflowResponseDto data { get; set; }
    }
}