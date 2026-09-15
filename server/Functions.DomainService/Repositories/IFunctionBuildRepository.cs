using Functions.DomainService.Entities;

namespace Functions.DomainService.Repositories
{
    public interface IFunctionBuildRepository
    {
        Task<FunctionBuildEntity?> GetByIdAsync(string tenantId, string buildId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The build cache lookup (DECISIONS D3): the most recent successful build for this
        /// exact source hash, if one exists. A hit lets Test and Deploy skip a rebuild entirely.
        /// </summary>
        Task<FunctionBuildEntity?> GetSucceededBySourceHashAsync(
            string tenantId, string functionId, string sourceHash, CancellationToken cancellationToken = default);

        /// <summary>
        /// A build already in flight for this source hash, if any — QUEUED or BUILDING.
        /// Lets a second Test click while a build is running wait on the same build instead
        /// of starting a redundant one.
        /// </summary>
        Task<FunctionBuildEntity?> GetInProgressBySourceHashAsync(
            string tenantId, string functionId, string sourceHash, CancellationToken cancellationToken = default);

        /// <summary>Every build recorded for one function — for cleanup, which needs their image digests.</summary>
        Task<IReadOnlyList<FunctionBuildEntity>> GetAllForFunctionAsync(
            string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>Removes every build record of one function. Returns how many went.</summary>
        Task<long> DeleteAllForFunctionAsync(string tenantId, string functionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks succeeded builds pointing at an image that is no longer there as failed, so the
        /// cache stops handing it out and the next Test or Deploy builds again. Without this a
        /// reclaimed image is a dead end: the record still succeeds, every run fails to pull it,
        /// and nothing in the product can force a rebuild.
        /// </summary>
        Task<long> InvalidateByImageDigestAsync(
            string tenantId, string imageDigest, string reason, CancellationToken cancellationToken = default);

        Task CreateAsync(string tenantId, FunctionBuildEntity build, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fails a build that never ran, but only while it is still QUEUED or BUILDING.
        /// <para>
        /// The dead-letter path, and conditional for the same reason the run one is: an entry can
        /// be dead-lettered after its build actually finished and reported, and overwriting a
        /// SUCCEEDED build with a failure would throw away a perfectly good image digest and send
        /// the next Test into a needless rebuild.
        /// </para>
        /// </summary>
        /// <returns>True when this call is what marked the build failed.</returns>
        Task<bool> FailIfNotTerminalAsync(
            string tenantId,
            string buildId,
            string errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default);

        Task<bool> ApplyResultAsync(
            string tenantId,
            string buildId,
            Enums.BuildStatus status,
            string? imageDigest,
            string? packages,
            string? log,
            string? errorMessage,
            DateTime completedAt,
            CancellationToken cancellationToken = default);
    }
}
