using Blocks.Genesis.Entities.Logic;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;

namespace Functions.DomainService.Services
{
    public interface IFunctionEventAllowList
    {
        /// <summary>The event types a run may send, for the Worker's second check before it sends.</summary>
        Task<List<string>> AllowedTypesAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The fixed rule for the run's trigger (the version's, or the function's for a Test run, which has no version):
    /// <see cref="FunctionEventPolicy.AllowedTypes"/>. A function that is gone is treated as Public: the narrowest answer.
    /// </summary>
    public sealed class FunctionEventAllowList : IFunctionEventAllowList
    {
        private readonly IFunctionVersionRepository _versions;
        private readonly IFunctionRepository _functions;

        public FunctionEventAllowList(IFunctionVersionRepository versions, IFunctionRepository functions)
        {
            _versions = versions;
            _functions = functions;
        }

        public async Task<List<string>> AllowedTypesAsync(
            string tenantId, FunctionRunEntity run, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);

            var version = string.IsNullOrEmpty(run.VersionId)
                ? null
                : await _versions.GetByIdAsync(tenantId, run.VersionId, cancellationToken);
            var trigger = version?.Trigger
                ?? (await _functions.GetByIdAsync(tenantId, run.FunctionId, cancellationToken))?.Trigger;

            return FunctionEventPolicy.AllowedTypes(trigger?.AuthMode ?? AuthMode.Public);
        }
    }
}
