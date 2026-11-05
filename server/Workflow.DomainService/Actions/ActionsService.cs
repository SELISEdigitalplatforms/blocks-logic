using Blocks.Genesis;

namespace Workflow.DomainService.Actions
{
    /// <summary>One action an editor may pick: what the workflow trigger node and the function Trigger section list.</summary>
    public sealed record ActionOptionDto(string Name, string Label, string Service, IReadOnlyDictionary<string, string> Payload);

    /// <summary>The editors' view of the tenant's actions: only those turned on for the target.</summary>
    public interface IActionsService
    {
        /// <summary>Actions with the Workflow switch on, sorted by service then label.</summary>
        Task<IReadOnlyList<ActionOptionDto>> GetForWorkflowAsync(CancellationToken cancellationToken = default);

        /// <summary>Actions with the Function switch on, sorted by service then label.</summary>
        Task<IReadOnlyList<ActionOptionDto>> GetForFunctionAsync(CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IActionsService"/>
    public sealed class ActionsService : IActionsService
    {
        private readonly IActionsRepository _actions;

        public ActionsService(IActionsRepository actions)
        {
            _actions = actions;
        }

        public Task<IReadOnlyList<ActionOptionDto>> GetForWorkflowAsync(CancellationToken cancellationToken = default) =>
            OptionsAsync(a => a.Workflow, cancellationToken);

        public Task<IReadOnlyList<ActionOptionDto>> GetForFunctionAsync(CancellationToken cancellationToken = default) =>
            OptionsAsync(a => a.Function, cancellationToken);

        private async Task<IReadOnlyList<ActionOptionDto>> OptionsAsync(
            Func<ActionDefinition, bool> enabled, CancellationToken cancellationToken)
        {
            var actions = await _actions.GetAllAsync(cancellationToken);
            return actions
                .Where(enabled)
                .OrderBy(a => a.Service, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
                .Select(a => new ActionOptionDto(a.Name, a.Label, a.Service, a.Payload ?? []))
                .ToList();
        }
    }
}
