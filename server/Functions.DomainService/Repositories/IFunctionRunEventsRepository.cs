using Functions.DomainService.Events;

namespace Functions.DomainService.Repositories
{
    /// <summary>What happened to the events of each run attempt (<see cref="FunctionRunEventsEntity"/>).</summary>
    public interface IFunctionRunEventsRepository
    {
        /// <summary>The document of one attempt, or null.</summary>
        Task<FunctionRunEventsEntity?> GetAsync(
            string tenantId, string runId, int attempt, CancellationToken cancellationToken = default);

        /// <summary>The document of the newest attempt that has one, or null.</summary>
        Task<FunctionRunEventsEntity?> GetLatestAsync(
            string tenantId, string runId, CancellationToken cancellationToken = default);

        /// <summary>Whether the document of one attempt exists (the Worker has finished with its events).</summary>
        Task<bool> ExistsAsync(string tenantId, string runId, int attempt, CancellationToken cancellationToken = default);

        /// <summary>Writes the document of one attempt; writing it again replaces it.</summary>
        Task SaveAsync(string tenantId, FunctionRunEventsEntity document, CancellationToken cancellationToken = default);

        /// <summary>Removes the documents of every run of one function. Returns how many went.</summary>
        Task<long> DeleteAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);
    }
}
