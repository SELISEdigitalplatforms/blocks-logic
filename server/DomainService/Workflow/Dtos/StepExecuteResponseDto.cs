using Blocks.Genesis;

namespace DomainService.Workflow.Dtos
{
    public class StepExecuteResponseDto : BaseMutationResponse
    {
        public string? Message { get; set; } = string.Empty;
        public string? Code { get; set; } = string.Empty;
    }
}