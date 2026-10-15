using Blocks.Genesis;
using DataAccess.InternalService;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Scheduler.DomainService.Dtos;
using Scheduler.DomainService.Dtos.Responses;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Repositories
{
    public class ScheduleRepository : IScheduleRepository
    {
        private const string CollectionName = "Schedules";

        private readonly ILogger<ScheduleRepository> _logger;
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly ILogicDataAccess _dataAccess;

        private Task<ReadScope<Schedule>> ReadAsync(FilterDefinition<Schedule> filter, string callSite, string? tenantId = null) =>
            _dataAccess.ReadAsync(LogicSchemas.Schedule, filter, $"ScheduleRepository.{callSite}", tenantId);

        public ScheduleRepository(ILogger<ScheduleRepository> logger,
                                 IDbContextProvider dbContextProvider,
                                 IBlocksSecret blocksSecret,
                                 ILogicDataAccess dataAccess)
        {
            _dataAccess = dataAccess;
            _dbContextProvider = dbContextProvider;
            _logger = logger;
            _blocksSecret = blocksSecret;
        }

        private IMongoCollection<Schedule> GetCollection()
            => _dbContextProvider.GetCollection<Schedule>(CollectionName);

        private IMongoCollection<Schedule> GetCollection(string tenantId)
            => string.IsNullOrWhiteSpace(tenantId)
                ? GetCollection()
                : _dbContextProvider.GetCollection<Schedule>(tenantId, CollectionName);

        public async Task<(List<Schedule>? Items, long TotalCount)> GetAllAsync(string searchKey, int pageNumber, int pageSize, ScheduleKind? scheduleKind = null)
        {
            FilterDefinition<Schedule> filter = string.IsNullOrEmpty(searchKey)
                ? Builders<Schedule>.Filter.Empty
                : Builders<Schedule>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(searchKey, "i"));
            if (scheduleKind.HasValue)
            {
                filter &= Builders<Schedule>.Filter.Eq(
                    x => x.Kind,
                    scheduleKind.Value);
            }

            var scope = await ReadAsync(filter, "GetAll");
            if (!scope.Granted) return (null, 0);

            long totalCount = await GetCollection().CountDocumentsAsync(scope.Filter);

            var items = await scope.Apply(GetCollection())
                .Skip(pageNumber * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items.Count > 0 ? items : null, totalCount);
        }
        public async Task<Schedule?> GetByIdAsync(string scheduleId, string tenantId = "")
        {
            var collection = GetCollection(tenantId);
            // The entity feeds UpdateAsync (whole-doc replace) in callers: read it whole, no projection.
            var scope = await _dataAccess.ReadAsync(LogicSchemas.Schedule, Builders<Schedule>.Filter.Where(schedule => schedule.ItemId == scheduleId),
                "ScheduleRepository.GetById", tenantId, useProjection: false);
            return scope.Granted ? await scope.Apply(collection).FirstOrDefaultAsync() : null;
        }

        public async Task CreateAsync(Schedule schedule)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.Schedule, "ScheduleRepository.Create"))
                throw new UnauthorizedAccessException("Schedule create is not allowed by the data access policy.");
            var collection = GetCollection();
            await collection.InsertOneAsync(schedule);
        }

        public async Task UpdateAsync(Schedule schedule)
        {
            var filter = await _dataAccess.EditFilterAsync(LogicSchemas.Schedule,
                Builders<Schedule>.Filter.Eq(x => x.ItemId, schedule.ItemId), "ScheduleRepository.Update");
            if (filter is null) return;
            await GetCollection().ReplaceOneAsync(filter, schedule);
        }

        public async Task<bool> DeleteAsync(string itemId, string tenantId = "")
        {
            var schedulesCollection = GetCollection(tenantId);

            try
            {
                var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.Schedule,
                    Builders<Schedule>.Filter.Eq(x => x.ItemId, itemId), "ScheduleRepository.Delete", tenantId);
                if (filter is null) return false;
                var result = await schedulesCollection.DeleteOneAsync(filter);
                if (result.DeletedCount > 0)
                {
                    _logger.LogInformation($"Schedule {itemId} is deleted.");
                    return true;
                }

                _logger.LogInformation($"Schedule {itemId} was not found in database for deletion.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
        }

        public async Task<List<SchedularDto>> GetSchedulesFromAllTenantsAsync()
        {
            var tenants = await _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                    .GetCollection<Tenant>("Tenants")
                    .Find(Builders<Tenant>.Filter.Eq(t => t.IsDisabled, false))
                    .ToListAsync();

            const int batchSize = 10;
            List<SchedularDto> schedularDtos = [];

            for (var i = 0; i < tenants.Count; i += batchSize)
            {
                var batch = tenants.Skip(i).Take(batchSize);
                var tasks = batch.Select(async tenant =>
                {
                    if (tenant.IsDisabled) return null;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.DbConnectionString) || string.IsNullOrWhiteSpace(tenant.DBName))
                            throw new InvalidOperationException("Tenant database placement is incomplete.");

                        var schedulesCollection = _dbContextProvider.GetDatabase(tenant.DbConnectionString, tenant.DBName)
                                                  .GetCollection<Schedule>(CollectionName);

                        var filter = Builders<Schedule>.Filter.Ne(x => x.IsActive, false);
                        // Per-tenant DB loop (startup / timer, no user): the helper takes its skip path
                        // (no-user-context or other-tenant-db) and returns the original filter.
                        var scope = await ReadAsync(filter, "GetSchedulesFromAllTenants", tenant.TenantId);
                        var schedules = scope.Granted ? await scope.Apply(schedulesCollection).ToListAsync() : [];

                        return schedules.Count > 0
                            ? new SchedularDto { TenantId = tenant.TenantId, Schedules = schedules }
                            : null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load schedules for tenant {TenantId}", tenant.TenantId);
                        return null;
                    }
                });

                var results = await Task.WhenAll(tasks);
                schedularDtos.AddRange(results.Where(x => x is not null)!);
            }

            return schedularDtos;
        }
    }
}
