using Microsoft.Extensions.Logging;
using Workflow.DomainService.Repositories;

namespace Functions.DomainService.Services
{
    /// <summary>A workflow step that invokes a function.</summary>
    public sealed record FunctionWorkflowReference(string WorkflowId, string Name, bool IsPublished);

    public interface IFunctionUsageService
    {
        /// <summary>
        /// Workflows whose function steps point at this function. Throws when it cannot tell:
        /// an empty list has to mean "nothing uses it", never "the question could not be asked".
        /// </summary>
        Task<IReadOnlyList<FunctionWorkflowReference>> GetWorkflowReferencesAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// What would break if a function went away.
    /// <para>
    /// A workflow step pointing at a deleted function fails at execution with
    /// <c>function '…' was not found</c> — legible, but only to whoever is watching the run at
    /// the time, which is nobody at 3am. Asking before the delete moves that discovery to the
    /// person doing the deleting.
    /// </para>
    /// </summary>
    public class FunctionUsageService : IFunctionUsageService
    {
        private readonly IWorkflowRepository _workflowRepository;
        private readonly ILogger<FunctionUsageService> _logger;

        public FunctionUsageService(
            IWorkflowRepository workflowRepository, ILogger<FunctionUsageService> logger)
        {
            _workflowRepository = workflowRepository;
            _logger = logger;
        }

        public async Task<IReadOnlyList<FunctionWorkflowReference>> GetWorkflowReferencesAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return [];

            try
            {
                var workflows = await _workflowRepository.GetWorkflowsUsingFunctionAsync(tenantId, functionId);

                return [.. workflows.Select(w => new FunctionWorkflowReference(
                    w.ItemId ?? string.Empty,
                    string.IsNullOrWhiteSpace(w.Name) ? "(unnamed workflow)" : w.Name,
                    w.IsPublished))];
            }
            catch (Exception ex)
            {
                // Deliberately not swallowed into an empty list. "Nothing uses this" is the answer
                // a delete acts on, and inventing it because the query failed is how the guard
                // would quietly stop guarding.
                _logger.LogError(ex, "Could not check workflow references of function {FunctionId}", functionId);
                throw;
            }
        }
    }
}
