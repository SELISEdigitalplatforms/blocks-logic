using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DataAccess.InternalService;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Scheduler.DomainService.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IScheduleExecutionRepository"/> over the per-tenant <c>ScheduleExecutions</c>
    /// collection. Written only by Scheduler.Worker; read by the Api for the caller's own DB. Rows are kept for all
    /// time: no TTL index, no cap. The two indexes are created once per tenant per process, best-effort, on first
    /// write (the ProxyExecutions pattern).
    /// <para>
    /// Read filters carry <c>ScheduleId</c> only, never <c>TenantId</c>: the collection is already per tenant, the
    /// caller's schedule guard has proven ownership, and an extra key outside the index would force a FETCH on
    /// every count. The outcome filter is an <c>$in</c> equality list on <c>Response.Status</c>, never a range, so
    /// the status index keeps the <c>FiredUtc</c> order (SORT_MERGE) instead of a blocking in-memory SORT.
    /// </para>
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class ScheduleExecutionRepository : IScheduleExecutionRepository
    {
        internal const string CollectionName = "ScheduleExecutions";
        internal const string ScheduleFiredIndex = "ix_schedexec_schedule_fired";
        internal const string ScheduleStatusFiredIndex = "ix_schedexec_schedule_status_fired";
        internal const string StatusPath = "Response." + ScheduleExecution.ResponseStatusKey;

        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogicDataAccess _dataAccess;
        private readonly ConcurrentDictionary<string, byte> _indexedTenants = new(StringComparer.Ordinal);

        public ScheduleExecutionRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }

        private Task<ReadScope<ScheduleExecution>> ReadAsync(FilterDefinition<ScheduleExecution> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.ScheduleExecution, filter, $"ScheduleExecutionRepository.{callSite}", tenantId: null, useProjection);

        private IMongoCollection<ScheduleExecution> GetCollection()
            => _dbContextProvider.GetCollection<ScheduleExecution>(CollectionName);

        private IMongoCollection<ScheduleExecution> GetCollection(string tenantId)
            => _dbContextProvider.GetCollection<ScheduleExecution>(tenantId, CollectionName);

        internal static IReadOnlyList<CreateIndexModel<ScheduleExecution>> IndexModels()
        {
            var keys = Builders<ScheduleExecution>.IndexKeys;
            return
            [
                new CreateIndexModel<ScheduleExecution>(
                    keys.Ascending(e => e.ScheduleId).Descending(e => e.FiredUtc).Descending(e => e.ItemId),
                    new CreateIndexOptions { Name = ScheduleFiredIndex }),
                new CreateIndexModel<ScheduleExecution>(
                    keys.Ascending(e => e.ScheduleId).Ascending(StatusPath).Descending(e => e.FiredUtc).Descending(e => e.ItemId),
                    new CreateIndexOptions { Name = ScheduleStatusFiredIndex }),
            ];
        }

        /// <summary>
        /// Creates the indexes once per tenant database. A <see cref="MongoCommandException"/> (an existing
        /// incompatible index, say) is swallowed so it never blocks the insert, and the guard is dropped so a later
        /// write retries.
        /// </summary>
        private async Task EnsureIndexesAsync(string tenantId, IMongoCollection<ScheduleExecution> collection, CancellationToken cancellationToken)
        {
            if (!_indexedTenants.TryAdd(tenantId, 0))
            {
                return;
            }

            try
            {
                await collection.Indexes.CreateManyAsync(IndexModels(), cancellationToken);
            }
            catch (MongoCommandException)
            {
                _indexedTenants.TryRemove(tenantId, out _);
            }
        }

        public async Task InsertAsync(ScheduleExecution execution, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(execution.TenantId))
            {
                throw new InvalidOperationException("TenantId is required for a schedule execution row.");
            }

            // Scheduler.Worker has no authenticated context: the helper takes its skip path (no extra reads).
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.ScheduleExecution, "ScheduleExecutionRepository.Insert", execution.TenantId))
                throw new UnauthorizedAccessException("Schedule execution create is not allowed by the data access policy.");
            await _dataAccess.PrepareWriteAsync(LogicSchemas.ScheduleExecution, execution, isInsert: true, "ScheduleExecutionRepository.Insert", execution.TenantId);
            var collection = GetCollection(execution.TenantId);
            await EnsureIndexesAsync(execution.TenantId, collection, cancellationToken);
            await collection.InsertOneAsync(execution, options: null, cancellationToken);
        }

        public async Task<List<ScheduleExecution>> GetPageAsync(ScheduleExecutionFilter filter, int pageNumber, int pageSize)
        {
            var scope = await ReadAsync(BuildFilter(filter), "GetPage");
            if (!scope.Granted) return [];

            var hint = HintFor(filter);
            var rows = await WithHintFallback(useHint =>
                GetCollection()
                    .Find(scope.Filter, new FindOptions { Hint = useHint ? hint : null })
                    // The resolver's projection wins when it has one; otherwise only the list columns.
                    .Project<ScheduleExecution>(scope.Projection ?? ListProjection)
                    .Sort(NewestFirst)
                    .Skip(pageNumber * pageSize)
                    .Limit(pageSize)
                    .ToListAsync());
            return scope.ForView(rows);
        }

        public async Task<long> CountAsync(ScheduleExecutionFilter filter)
        {
            var scope = await ReadAsync(BuildFilter(filter), "Count");
            if (!scope.Granted) return 0;

            var hint = HintFor(filter);
            return await WithHintFallback(useHint =>
                GetCollection().CountDocumentsAsync(scope.Filter, new CountOptions { Hint = useHint ? hint : null }));
        }

        public async Task<ScheduleExecution?> GetByIdAsync(string scheduleId, string executionId)
        {
            var scope = await ReadAsync(ByIdFilter(scheduleId, executionId), "GetById");
            return scope.Granted ? scope.ForView(await scope.Apply(GetCollection()).FirstOrDefaultAsync()) : null;
        }

        public async Task<IReadOnlyList<int>> GetDistinctStatusesAsync(string scheduleId)
        {
            var scope = await ReadAsync(BuildFilter(new ScheduleExecutionFilter(scheduleId)), "GetDistinctStatuses", useProjection: false);
            if (!scope.Granted) return [];

            var cursor = await GetCollection().DistinctAsync(StatusField, scope.Filter);
            var values = await cursor.ToListAsync();
            return values.Select(ToStatus).OfType<int>().Distinct().ToList();
        }

        // ---------- Query shapes (rendered in the tests) ----------

        internal static readonly FieldDefinition<ScheduleExecution, BsonValue> StatusField =
            new StringFieldDefinition<ScheduleExecution, BsonValue>(StatusPath);

        internal static SortDefinition<ScheduleExecution> NewestFirst =>
            Builders<ScheduleExecution>.Sort
                .Descending(e => e.FiredUtc)
                .Descending(e => e.ItemId);

        internal static ProjectionDefinition<ScheduleExecution> ListProjection =>
            Builders<ScheduleExecution>.Projection
                .Include(e => e.ItemId)
                .Include(e => e.FiredUtc)
                .Include(e => e.FinishedUtc)
                .Include(StatusPath);

        internal static BsonValue HintFor(ScheduleExecutionFilter filter) =>
            new BsonString(filter.HasOutcome ? ScheduleStatusFiredIndex : ScheduleFiredIndex);

        internal static FilterDefinition<ScheduleExecution> BuildFilter(ScheduleExecutionFilter filter)
        {
            var builder = Builders<ScheduleExecution>.Filter;
            var result = builder.Eq(e => e.ScheduleId, filter.ScheduleId);

            if (filter.Statuses is not null)
            {
                result &= builder.In<int>(StatusPath, filter.Statuses);
            }
            else if (filter.NonSuccessRange)
            {
                result &= builder.Or(builder.Lt<int>(StatusPath, 200), builder.Gt<int>(StatusPath, 299));
            }

            if (filter.StartUtc is { } start)
            {
                result &= builder.Gte(e => e.FiredUtc, start);
            }

            if (filter.EndUtc is { } end)
            {
                result &= builder.Lt(e => e.FiredUtc, end);
            }

            return result;
        }

        internal static FilterDefinition<ScheduleExecution> ByIdFilter(string scheduleId, string executionId)
        {
            var builder = Builders<ScheduleExecution>.Filter;
            return builder.Eq(e => e.ScheduleId, scheduleId) & builder.Eq(e => e.ItemId, executionId);
        }

        internal static int? ToStatus(BsonValue value) => value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 when value.AsInt64 is >= int.MinValue and <= int.MaxValue => (int)value.AsInt64,
            _ => null,
        };

        /// <summary>
        /// Runs a hinted query, and once more without the hint when the server refuses it because the index does
        /// not exist (a tenant DB whose first write failed to create it). The answer is the same, only slower.
        /// </summary>
        private static async Task<T> WithHintFallback<T>(Func<bool, Task<T>> query)
        {
            try
            {
                return await query(true);
            }
            catch (MongoCommandException ex) when (ex.Message.Contains("hint", StringComparison.OrdinalIgnoreCase))
            {
                return await query(false);
            }
        }
    }
}
