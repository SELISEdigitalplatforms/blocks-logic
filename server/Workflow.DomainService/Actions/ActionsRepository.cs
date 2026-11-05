using Blocks.Genesis;

namespace Workflow.DomainService.Actions
{
    /// <summary>
    /// The context tenant's <c>actions</c> list (<see cref="ActionConstants.ActionsKey"/>). Read only: blocks-os owns it and
    /// is its only writer. Read directly every time (no cache), so a switch turned off in OS takes effect on the next
    /// message. Data access only.
    /// </summary>
    public interface IActionsRepository
    {
        /// <summary>The tenant's actions; empty when the tenant has none.</summary>
        Task<IReadOnlyList<ActionDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>The action named <paramref name="name"/> (ordinal match), or null when the tenant has no such action.</summary>
        Task<ActionDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IActionsRepository"/>
    public sealed class ActionsRepository : IActionsRepository
    {
        private readonly IKeyValueStore _keyValueStore;

        public ActionsRepository(IKeyValueStore keyValueStore)
        {
            _keyValueStore = keyValueStore;
        }

        // impersonated: true = the context tenant's database (the project), also for an impersonated root user.
        public async Task<IReadOnlyList<ActionDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var actions = await _keyValueStore.GetAsync<List<ActionDefinition>>(
                ActionConstants.ActionsKey, impersonated: true, cancellationToken);
            return actions?.Where(a => a is not null && !string.IsNullOrEmpty(a.Name)).ToList() ?? [];
        }

        public async Task<ActionDefinition?> FindAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var actions = await GetAllAsync(cancellationToken);
            return actions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        }
    }
}
