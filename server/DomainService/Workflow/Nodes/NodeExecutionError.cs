namespace DomainService.Workflow.Nodes
{

    public class NodeExecutionError
    {

        public required string ErrorMessage { get; init; } = default!;
        public required string ErrorCode { get; init; } = default!;

    }
}