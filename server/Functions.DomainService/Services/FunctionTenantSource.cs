using Blocks.Genesis;
using MongoDB.Driver;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Every tenant a Worker-side sweep has to visit. blocks-logic is database-per-tenant, so
    /// background work that is not driven by a message — like <c>FunctionStaleRunSweeper</c> —
    /// has to enumerate tenants itself. Behind an interface so the sweep is testable without a
    /// root database.
    /// </summary>
    public interface IFunctionTenantSource
    {
        /// <summary>Ids of every tenant that is not disabled.</summary>
        Task<IReadOnlyList<string>> GetActiveTenantIdsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads the root database's <c>Tenants</c> collection, the same way the Scheduler
    /// (<c>ScheduleRepository.GetSchedulesFromAllTenantsAsync</c>) and the mailbox sync
    /// (<c>MailRepository.GetTenantsAsync</c>) enumerate tenants: enabled tenants only.
    /// </summary>
    public sealed class FunctionTenantSource : IFunctionTenantSource
    {
        private const string TenantsCollection = "Tenants";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public FunctionTenantSource(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        public async Task<IReadOnlyList<string>> GetActiveTenantIdsAsync(CancellationToken cancellationToken = default)
        {
            var ids = await _dbContextProvider
                .GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                .GetCollection<Tenant>(TenantsCollection)
                .Find(Builders<Tenant>.Filter.Eq(t => t.IsDisabled, false))
                .Project(t => t.TenantId)
                .ToListAsync(cancellationToken);

            return ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}
