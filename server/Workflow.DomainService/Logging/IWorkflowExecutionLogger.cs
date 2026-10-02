using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Logging
{
    /// <summary>
    /// Writes the execution stage lines shown in the execution logs panel. Every line is stamped with the
    /// execution's <see cref="WorkflowExecutionEntity.TraceId"/> so it can be fetched by that id. Lines
    /// describe stages and counts only, never parameter or item values.
    /// </summary>
    public interface IWorkflowExecutionLogger
    {
        /// <summary>Bound to one execution. Cheap; create per use.</summary>
        ExecutionLog For(WorkflowExecutionEntity execution);
    }
}
