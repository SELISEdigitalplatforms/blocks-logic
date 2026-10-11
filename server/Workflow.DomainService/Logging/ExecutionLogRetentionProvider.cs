using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Workflow.DomainService.Logging
{
    /// <summary>How many days execution log lines stay readable before LMT archives them.</summary>
    public interface IExecutionLogRetentionProvider
    {
        /// <summary>Always a positive number of days. Never throws.</summary>
        Task<int> GetRetentionDaysAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Reads <c>HotDataRetentionPeriodInDays</c> from the single document in the root database's
    /// <c>LmtArchiveRestoreConfigurations</c> collection (owned by blocks-os), cached for 10 minutes. Falls back to
    /// <see cref="ExecutionLogOptions.RetentionDays"/> when the value is missing, not positive, or unreadable.
    /// </summary>
    public class LmtExecutionLogRetentionProvider : IExecutionLogRetentionProvider
    {
        public const string CollectionName = "LmtArchiveRestoreConfigurations";
        public const string FieldName = "HotDataRetentionPeriodInDays";
        internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        private readonly IDbContextProvider? _dbContextProvider;
        private readonly IBlocksSecret? _blocksSecret;
        private readonly ExecutionLogOptions _options;
        private readonly ILogger<LmtExecutionLogRetentionProvider> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private int _cachedDays;
        private DateTimeOffset _cachedUntil = DateTimeOffset.MinValue;

        public LmtExecutionLogRetentionProvider(
            IDbContextProvider dbContextProvider,
            IBlocksSecret blocksSecret,
            IOptions<ExecutionLogOptions> options,
            ILogger<LmtExecutionLogRetentionProvider> logger,
            TimeProvider timeProvider)
            : this(options, logger, timeProvider)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        /// <summary>For subclasses that replace <see cref="ReadHotRetentionDaysAsync"/> (tests).</summary>
        protected LmtExecutionLogRetentionProvider(
            IOptions<ExecutionLogOptions> options,
            ILogger<LmtExecutionLogRetentionProvider> logger,
            TimeProvider timeProvider)
        {
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider;
        }

        public async Task<int> GetRetentionDaysAsync(CancellationToken ct = default)
        {
            if (_timeProvider.GetUtcNow() < _cachedUntil) return _cachedDays;

            await _refreshLock.WaitAsync(ct);
            try
            {
                if (_timeProvider.GetUtcNow() < _cachedUntil) return _cachedDays;

                int? days = null;
                try
                {
                    days = await ReadHotRetentionDaysAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not read {Field} from {Collection}; using the configured fallback.", FieldName, CollectionName);
                }

                if (days is not > 0)
                {
                    if (days is not null)
                    {
                        _logger.LogWarning("{Field} in {Collection} is {Value}; using the configured fallback.", FieldName, CollectionName, days);
                    }
                    else
                    {
                        _logger.LogWarning("{Field} was not found in {Collection}; using the configured fallback.", FieldName, CollectionName);
                    }
                    days = _options.RetentionDays > 0 ? _options.RetentionDays : 30;
                }

                _cachedDays = days.Value;
                _cachedUntil = _timeProvider.GetUtcNow() + CacheDuration;
                return _cachedDays;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>The raw value, or null when the document or the field is missing.</summary>
        protected virtual async Task<int?> ReadHotRetentionDaysAsync(CancellationToken ct)
        {
            if (_dbContextProvider is null || _blocksSecret is null) return null;

            var document = await _dbContextProvider
                .GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                .GetCollection<BsonDocument>(CollectionName)
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Project(Builders<BsonDocument>.Projection.Include(FieldName))
                .FirstOrDefaultAsync(ct);

            if (document is null || !document.TryGetValue(FieldName, out var value) || !value.IsNumeric) return null;
            return value.ToInt32();
        }
    }
}
