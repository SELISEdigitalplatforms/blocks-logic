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

        Task CreateAsync(string tenantId, FunctionBuildEntity build, CancellationToken cancellationToken = default);

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
