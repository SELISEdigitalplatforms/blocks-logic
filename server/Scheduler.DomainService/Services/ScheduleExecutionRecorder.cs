using Blocks.Genesis.Entities.Logic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Utils;

namespace Scheduler.DomainService.Services
{
    /// <summary>
    /// <see cref="IScheduleExecutionRecorder"/> over <see cref="IScheduleExecutionRepository"/>: one insert with a hard
    /// 5 s timeout. Any failure (Mongo down, timeout, policy refusal) is logged as one warning and swallowed.
    /// </summary>
    public sealed class ScheduleExecutionRecorder : IScheduleExecutionRecorder
    {
        internal static readonly TimeSpan DefaultInsertTimeout = TimeSpan.FromSeconds(5);

        private readonly IScheduleExecutionRepository _repository;
        private readonly ScheduleExecutionHistoryOptions _options;
        private readonly ILogger<ScheduleExecutionRecorder> _logger;
        private readonly TimeSpan _insertTimeout;

        public ScheduleExecutionRecorder(
            IScheduleExecutionRepository repository,
            IOptions<ScheduleExecutionHistoryOptions> options,
            ILogger<ScheduleExecutionRecorder> logger)
            : this(repository, options, logger, DefaultInsertTimeout)
        {
        }

        internal ScheduleExecutionRecorder(
            IScheduleExecutionRepository repository,
            IOptions<ScheduleExecutionHistoryOptions> options,
            ILogger<ScheduleExecutionRecorder> logger,
            TimeSpan insertTimeout)
        {
            _repository = repository;
            _options = options.Value;
            _logger = logger;
            _insertTimeout = insertTimeout;
        }

        internal TimeSpan InsertTimeout => _insertTimeout;

        public async Task RecordAsync(ScheduleExecution execution)
        {
            if (!_options.Enabled)
            {
                return;
            }

            using var cts = new CancellationTokenSource(_insertTimeout);
            try
            {
                // WaitAsync bounds the whole call, including the data-access steps that take no token.
                await _repository.InsertAsync(execution, cts.Token).WaitAsync(cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Schedule execution history write failed for schedule {ScheduleId} (tenant {TenantId}).",
                    execution.ScheduleId, execution.TenantId);
            }
        }
    }
}
