using Blocks.Genesis;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Carries the run counters of the retired <c>FunctionRunStats</c> collection onto the
    /// functions themselves, then drops the collection. Runs once per Worker start and is a no-op
    /// for a tenant whose collection is already gone, so it can stay until every environment has
    /// been through it, and then be deleted.
    /// <para>
    /// Each old document is <i>taken</i> (find-and-delete) before its counts are added, and put
    /// back if the add fails. So a second Worker, or a restart halfway through, never adds the
    /// same counts twice. The one window it leaves — the process dying between the take and the
    /// add — undercounts one function rather than double-counting it, and its values are in the
    /// log line written just before.
    /// </para>
    /// <para>
    /// Additive on purpose: runs started since the new code shipped have already been counted on
    /// the function, and adding the old total to them is exactly the all-time count.
    /// </para>
    /// </summary>
    public sealed class FunctionRunStatsMigration : BackgroundService
    {
        private const string LockKey = "functions:runstats-migration:lock";

        /// <summary>The shape of a retired counter document. <c>ItemId</c> is the function id.</summary>
        [BsonIgnoreExtraElements]
        internal sealed class LegacyRunStats : BaseEntity
        {
            public long TotalRuns { get; set; }
            public DateTime? LastRunAt { get; set; }
        }

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IFunctionRepository _functions;
        private readonly IFunctionTenantSource _tenantSource;
        private readonly IDatabase _db;
        private readonly ILogger<FunctionRunStatsMigration> _logger;
        private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

        public FunctionRunStatsMigration(
            IDbContextProvider dbContextProvider,
            IFunctionRepository functions,
            IFunctionTenantSource tenantSource,
            ICacheClient cache,
            ILogger<FunctionRunStatsMigration> logger)
        {
            _dbContextProvider = dbContextProvider;
            _functions = functions;
            _tenantSource = tenantSource;
            _db = cache.CacheDatabase();
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Not in the startup path: a Worker that is restarting to recover from something
                // else should not also be walking every tenant database straight away.
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                if (!await _db.LockTakeAsync(LockKey, _owner, TimeSpan.FromMinutes(30)))
                {
                    _logger.LogInformation("Another Worker is carrying over the old run counters; skipping");
                    return;
                }

                try
                {
                    var tenants = await _tenantSource.GetActiveTenantIdsAsync(stoppingToken);
                    foreach (var tenantId in tenants)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        try
                        {
                            await MigrateTenantAsync(tenantId, stoppingToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // Left for the next Worker start; nothing is lost, the collection stays.
                            _logger.LogError(ex, "Could not carry over the run counters of tenant {TenantId}", tenantId);
                        }
                    }
                }
                finally
                {
                    await _db.LockReleaseAsync(LockKey, _owner);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The run-counter carry-over failed; it will be tried again on the next Worker start");
            }
        }

        /// <summary>Moves one tenant's counters. Returns how many documents it carried over.</summary>
        internal async Task<int> MigrateTenantAsync(string tenantId, CancellationToken cancellationToken)
        {
            var collection = _dbContextProvider.GetCollection<LegacyRunStats>(
                tenantId, FunctionsConstants.LegacyRunStatsCollection);

            var moved = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stats = await collection.FindOneAndDeleteAsync(
                    FilterDefinition<LegacyRunStats>.Empty, cancellationToken: cancellationToken);
                if (stats is null) break;

                _logger.LogInformation(
                    "Carrying over run counters of function {FunctionId} (tenant {TenantId}): {TotalRuns} run(s), last {LastRunAt}",
                    stats.ItemId, tenantId, stats.TotalRuns, stats.LastRunAt);
                try
                {
                    await _functions.ImportRunCountersAsync(
                        tenantId, stats.ItemId, stats.TotalRuns, stats.LastRunAt, cancellationToken);
                }
                catch
                {
                    // Put it back so the next attempt carries it over; then stop this tenant.
                    await collection.InsertOneAsync(stats, cancellationToken: CancellationToken.None);
                    throw;
                }
                moved++;
            }

            // Empty now (or never existed): drop it, so the database no longer shows it at all.
            await collection.Database.DropCollectionAsync(FunctionsConstants.LegacyRunStatsCollection, cancellationToken);
            if (moved > 0)
            {
                _logger.LogInformation(
                    "Carried over {Count} run-counter document(s) for tenant {TenantId} and dropped {Collection}",
                    moved, tenantId, FunctionsConstants.LegacyRunStatsCollection);
            }
            return moved;
        }
    }
}
