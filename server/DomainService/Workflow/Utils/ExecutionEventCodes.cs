using DomainService.Workflow.Enums;

namespace DomainService.Workflow.Utils
{
    public static class ExecutionEventCodes
    {
        private const string WorkflowPrefix = "WF";
        private const string NodePrefix = "ND";

        public static string WorkflowExecutionCode(WorkflowExecutionStatus status)
            => $"{WorkflowPrefix}{(int)status:D3}";

        public static string NodeExecutionCode(NodeExecutionStatus status)
            => $"{NodePrefix}{(int)status:D3}";
    }
}