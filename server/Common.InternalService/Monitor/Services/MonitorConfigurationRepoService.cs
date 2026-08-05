using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Common.InternalService.Monitor
{
    public class MonitorConfigurationRepoService : IMonitorConfigurationRepoService
    {
        private readonly ILogger<MonitorConfigurationRepoService> _logger;
        private readonly IMongoCollection<MonitorConfiguration> _monitorConfigurationCollection;

        public MonitorConfigurationRepoService(ILogger<MonitorConfigurationRepoService> logger, IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            var db = dbContextProvider.GetDatabase(blocksSecret.DatabaseConnectionString, blocksSecret.RootDatabaseName);

            _logger = logger;
            _monitorConfigurationCollection = db.GetCollection<MonitorConfiguration>("MonitorConfigurations");
        }

        public async Task<bool> DeleteConfigurationAsync(string itemId)
        {
            try
            {
                var filter = Builders<MonitorConfiguration>.Filter.Eq(m => m.ItemId, itemId);
                var result = await _monitorConfigurationCollection.DeleteOneAsync(filter);

                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting MonitorConfiguration with ItemId {ItemId}", itemId);
                return false;
            }
        }

        public async Task<MonitorConfiguration?> GetConfigurationAsync(string itemId)
        {
            try
            {
                var filter = Builders<MonitorConfiguration>.Filter.Eq(m => m.ItemId, itemId);
                var monitorConfig = await _monitorConfigurationCollection.Find(filter).FirstOrDefaultAsync();

                return monitorConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MonitorConfiguration with ItemId {ItemId}", itemId);
                return null;
            }
        }

        public async Task<(List<MonitorConfiguration> Items, int TotalCount)> GetConfigurationListAsync(string tenantId, string? monitorSourceType, int pageNumber, int pageSize, string? sortProperty = null, bool sortIsDescending = false)
        {
            try
            {
                var fb = Builders<MonitorConfiguration>.Filter;
                FilterDefinition<MonitorConfiguration> filter;

                if (!string.IsNullOrWhiteSpace(monitorSourceType))
                {
                    if (!Enum.TryParse<MonitorSourceTypes>(monitorSourceType, true, out var parsedType))
                    {
                        _logger.LogWarning("Invalid MonitorSourceType value: {MonitorSourceType}", monitorSourceType);
                        return (new List<MonitorConfiguration>(), 0);
                    }

                    var sourceTypeFilter = fb.Or(
                        fb.Eq(m => m.MonitorSourceType, parsedType),
                        fb.Eq(m => m.LegacyMonitorSourceType, parsedType)
                    );

                    if (parsedType == MonitorSourceTypes.Infrastructure ||
                        parsedType == MonitorSourceTypes.BlocksServices)
                    {
                        filter = sourceTypeFilter;
                    }
                    else
                    {
                        filter = fb.And(
                            sourceTypeFilter,
                            fb.Eq(m => m.TenantId, tenantId)
                        );
                    }
                }
                else
                {
                    var tenantScopedTypes = new[]
                    {
                        MonitorSourceTypes.DeployedServices,
                        MonitorSourceTypes.ExternalServices,
                        MonitorSourceTypes.OtherServices,
                    };
                    var tenantScopedTypesNullable = tenantScopedTypes
                        .Select(type => (MonitorSourceTypes?)type)
                        .ToArray();

                    filter = fb.Or(
                        fb.Eq(m => m.MonitorSourceType, MonitorSourceTypes.Infrastructure),
                        fb.Eq(m => m.LegacyMonitorSourceType, MonitorSourceTypes.Infrastructure),

                        fb.And(
                            fb.Or(
                                fb.In(m => m.MonitorSourceType, tenantScopedTypesNullable),
                                fb.In(m => m.LegacyMonitorSourceType, tenantScopedTypesNullable)
                            ),
                            fb.Eq(m => m.TenantId, tenantId)
                        )
                    );
                }

                pageNumber = pageNumber < 0 ? 0 : pageNumber;
                pageSize = pageSize <= 0 ? 10 : pageSize;

                var skip = pageNumber * pageSize;

                var totalCount = (int)await _monitorConfigurationCollection.CountDocumentsAsync(filter);

                var sort = BuildSortDefinition(monitorSourceType, sortProperty, sortIsDescending);

                var items = await _monitorConfigurationCollection
                    .Find(filter)
                    .Sort(sort)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync();

                return (items, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error retrieving MonitorConfigurations for TenantId {TenantId} and MonitorSourceType {MonitorSourceType}",
                    tenantId,
                    monitorSourceType);

                return (new List<MonitorConfiguration>(), 0);
            }
        }

        private static SortDefinition<MonitorConfiguration> BuildSortDefinition(
            string? monitorSourceType,
            string? sortProperty,
            bool sortIsDescending)
        {
            var sortBuilder = Builders<MonitorConfiguration>.Sort;
            var direction = sortIsDescending
                ? SortDirection.Descending
                : SortDirection.Ascending;

            return (sortProperty ?? string.Empty).ToLowerInvariant() switch
            {
                "monitor_type" => direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.MonitorConfigurationType)
                    : sortBuilder.Ascending(x => x.MonitorConfigurationType),
                "url" => direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.Url)
                    : sortBuilder.Ascending(x => x.Url),
                "tagged_service" => BuildTaggedServiceSort(sortBuilder, monitorSourceType, direction),
                "uptime" => direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.LastIncidentAt)
                    : sortBuilder.Ascending(x => x.LastIncidentAt),
                "status" => direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.CurrentStatus)
                    : sortBuilder.Ascending(x => x.CurrentStatus),
                _ => direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.Name)
                    : sortBuilder.Ascending(x => x.Name),
            };
        }

        private static SortDefinition<MonitorConfiguration> BuildTaggedServiceSort(
            SortDefinitionBuilder<MonitorConfiguration> sortBuilder,
            string? monitorSourceType,
            SortDirection direction)
        {
            if (string.Equals(monitorSourceType, MonitorSourceTypes.DeployedServices.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.RepoName)
                    : sortBuilder.Ascending(x => x.RepoName);
            }

            if (string.Equals(monitorSourceType, MonitorSourceTypes.ExternalServices.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return direction == SortDirection.Descending
                    ? sortBuilder.Descending(x => x.ExternalServiceName)
                    : sortBuilder.Ascending(x => x.ExternalServiceName);
            }

            return direction == SortDirection.Descending
                ? sortBuilder.Descending(x => x.RepoName)
                : sortBuilder.Ascending(x => x.RepoName);
        }

        public async Task<List<MonitorConfiguration>> GetConfigurationListByRepoIdAsync(string tenantId, string repoId)
        {
            try
            {
                var filter = Builders<MonitorConfiguration>.Filter.And(
                    Builders<MonitorConfiguration>.Filter.Eq(m => m.TenantId, tenantId),
                    Builders<MonitorConfiguration>.Filter.Eq(m => m.RepoId, repoId)
                );
                var monitorConfig = await _monitorConfigurationCollection.Find(filter).ToListAsync();

                return monitorConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MonitorConfigurations for TenantId {TenantId}", tenantId);
                return new List<MonitorConfiguration>();
            }
        }

        public async Task<bool> SaveConfigurationAsync(MonitorConfiguration monitorConfiguration)
        {
            try
            {
                var filter = Builders<MonitorConfiguration>.Filter.Eq(m => m.ItemId, monitorConfiguration.ItemId);

                var result = await _monitorConfigurationCollection.ReplaceOneAsync(
                    filter,
                    monitorConfiguration,
                    new ReplaceOptions { IsUpsert = true }
                );

                return result.UpsertedId != null;
            }
            catch (MongoWriteException e)
            {
                _logger.LogError(e, "Error saving MonitorConfiguration");
                return false;
            }
        }

        public async Task<List<MonitorConfiguration>> GetConfigurationListByTenantIdAsync(string tenantId)
        {
            try
            {
                var filter = Builders<MonitorConfiguration>.Filter.Eq(m => m.TenantId, tenantId);

                var monitorConfigs = await _monitorConfigurationCollection
                    .Find(filter)
                    .ToListAsync();

                return monitorConfigs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error retrieving MonitorConfigurations for TenantId {TenantId}",
                    tenantId);

                return new List<MonitorConfiguration>();
            }
        }

        public async Task<MonitorConfiguration> GetByUrlAsync(string url)
        {
            var filter = Builders<MonitorConfiguration>.Filter.Eq(m => m.Url, url);
            return await _monitorConfigurationCollection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<MonitorConfiguration?> GetExternalServiceConfigurationAsync(string externalServiceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(externalServiceId))
                    return null;

                var filter = Builders<MonitorConfiguration>.Filter.And(
                    Builders<MonitorConfiguration>.Filter.Or(
                        Builders<MonitorConfiguration>.Filter.Eq(m => m.MonitorSourceType, MonitorSourceTypes.ExternalServices),
                        Builders<MonitorConfiguration>.Filter.Eq(m => m.LegacyMonitorSourceType, MonitorSourceTypes.ExternalServices)
                    ),
                    Builders<MonitorConfiguration>.Filter.Eq(m => m.ExternalServiceId, externalServiceId)
                );

                return await _monitorConfigurationCollection
                    .Find(filter)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error retrieving MonitorConfiguration for ExternalServiceId {ExternalServiceId}",
                    externalServiceId);

                return null;
            }
        }
    }
}
