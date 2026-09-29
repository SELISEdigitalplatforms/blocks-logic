using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Repositories;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Which output actions, under which retry policy, apply to a run: the deployed version's
    /// when the run has one, otherwise the function's own (a Test run). Shared by the result
    /// consumer, which only needs to know whether there is anything to deliver, and the output
    /// consumer, which delivers it — so the two can never disagree about that.
    /// </summary>
    internal static class FunctionOutputConfig
    {
        public static async Task<(List<OutputAction> Actions, RetryPolicy RetryPolicy)> ResolveAsync(
            IFunctionVersionRepository versionRepository,
            IFunctionRepository functionRepository,
            string tenantId,
            FunctionRunEntity run,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(run.VersionId))
            {
                var version = await versionRepository.GetByIdAsync(tenantId, run.VersionId, cancellationToken);
                if (version is not null) return (version.OutputActions ?? [], version.Retry ?? new RetryPolicy());
            }

            var function = await functionRepository.GetByIdAsync(tenantId, run.FunctionId, cancellationToken);
            return (function?.OutputActions ?? [], function?.Retry ?? new RetryPolicy());
        }

        /// <summary>True when at least one action would actually be attempted.</summary>
        public static bool HasEnabledActions(IReadOnlyCollection<OutputAction> actions)
            => actions.Any(a => a is { Enabled: true });
    }
}
