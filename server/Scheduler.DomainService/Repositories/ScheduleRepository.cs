using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Scheduler.DomainService.Dtos;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Enums;

namespace Scheduler.DomainService.Repositories
{
    public class ScheduleRepository : IScheduleRepository
    {
        private const string CollectionName = "Schedules";

        private readonly ILogger<ScheduleRepository> _logger;
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public ScheduleRepository(ILogger<ScheduleRepository> logger,
                                 IDbContextProvider dbContextProvider,
                                 IBlocksSecret blocksSecret)
        {
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

            long totalCount = await GetCollection().CountDocumentsAsync(filter);

            var items = await GetCollection()
                .Find(filter)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items.Count > 0 ? items : null, totalCount);
        }
        public Task<Schedule?> GetByIdAsync(string scheduleId, string tenantId = "")
        {
            var collection = GetCollection(tenantId);
            return collection.Find(schedule => schedule.ItemId == scheduleId).FirstOrDefaultAsync();
        }

        public Task CreateAsync(Schedule schedule)
        {
            var collection = GetCollection();
            return collection.InsertOneAsync(schedule);
        }

        public Task UpdateAsync(Schedule schedule)
        {
            return GetCollection().ReplaceOneAsync(
                Builders<Schedule>.Filter.Eq(x => x.ItemId, schedule.ItemId),
                schedule);
        }

        public async Task<bool> DeleteAsync(string itemId, string tenantId = "")
        {
            var schedulesCollection = GetCollection(tenantId);

            try
            {
                var result = await schedulesCollection.DeleteOneAsync(Builders<Schedule>.Filter.Eq(x => x.ItemId, itemId));
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
            var tenants = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksRootDb")
                    .GetCollection<Tenant>("Tenants")
                    .Find(FilterDefinition<Tenant>.Empty)
                    .ToList();

            const int batchSize = 10;
            List<SchedularDto> schedularDtos = [];

            for (var i = 0; i < tenants.Count; i += batchSize)
            {
                var batch = tenants.Skip(i).Take(batchSize);
                var tasks = batch.Select(async tenant =>
                {
                    try
                    {
                        var schedulesCollection = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, tenant.DBName)
                                                  .GetCollection<Schedule>(CollectionName);

                        var filter = Builders<Schedule>.Filter.Ne(x => x.IsActive, false);
                        var schedules = await schedulesCollection.Find(filter).ToListAsync();

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
