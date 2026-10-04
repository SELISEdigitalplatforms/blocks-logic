using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Repositories;

namespace Workflow.DomainService.Logging
{
    public interface IExecutionLogService
    {
        /// <summary>The parsed stage lines of one execution of the caller's tenant.</summary>
        Task<WorkflowExecutionLogsGetResponseDto> GetAsync(string tenantId, WorkflowExecutionLogsGetRequestDto dto, CancellationToken ct = default);
    }

    /// <inheritdoc />
    public sealed class ExecutionLogService : IExecutionLogService
    {
        private static readonly TimeSpan WindowLeadIn = TimeSpan.FromMinutes(1);

        private readonly IWorkflowExecutionRepository _executionRepository;
        private readonly IExecutionLogStore _store;
        private readonly IExecutionLogRetentionProvider _retentionProvider;
        private readonly ExecutionLogOptions _options;
        private readonly ILogger<ExecutionLogService> _logger;
        private readonly TimeProvider _timeProvider;

        public ExecutionLogService(
            IWorkflowExecutionRepository executionRepository,
            IExecutionLogStore store,
            IExecutionLogRetentionProvider retentionProvider,
            IOptions<ExecutionLogOptions> options,
            ILogger<ExecutionLogService> logger,
            TimeProvider timeProvider)
        {
            _executionRepository = executionRepository;
            _store = store;
            _retentionProvider = retentionProvider;
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider;
        }

        public async Task<WorkflowExecutionLogsGetResponseDto> GetAsync(string tenantId, WorkflowExecutionLogsGetRequestDto dto, CancellationToken ct = default)
        {
            // Tenant check: the lookup is scoped to the caller's tenant, same not-found result as GetExecution.
            var execution = await _executionRepository.GetByIdAsync(dto.ExecutionId, tenantId)
                ?? throw new InvalidOperationException($"Execution {dto.ExecutionId} not found");

            var retentionDays = await _retentionProvider.GetRetentionDaysAsync(ct);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var grace = TimeSpan.FromSeconds(Math.Max(0, _options.IngestionGraceSeconds));

            var data = new WorkflowExecutionLogsDto
            {
                ExecutionId = execution.Id,
                TraceId = execution.TraceId,
                Availability = ExecutionLogsAvailability.Available,
                RetentionDays = retentionDays,
            };

            if (string.IsNullOrEmpty(execution.TraceId))
            {
                data.Availability = ExecutionLogsAvailability.NotRecorded;
                return Success(data);
            }

            data.ExpiresAt = execution.StartedAt.AddDays(retentionDays);
            if (execution.StartedAt < now.AddDays(-retentionDays))
            {
                data.Availability = ExecutionLogsAvailability.Expired;
                return Success(data);
            }

            var maxLines = Math.Max(1, _options.MaxLines);
            IReadOnlyList<RawExecutionLogLine> rawLines;
            try
            {
                rawLines = await _store.QueryAsync(
                    new ExecutionLogQuery(
                        execution.TenantId,
                        execution.TraceId,
                        execution.StartedAt - WindowLeadIn,
                        (execution.FinishedAt ?? now) + grace,
                        maxLines),
                    ct);
            }
            catch (ExecutionLogStoreNotConfiguredException ex)
            {
                _logger.LogWarning("Execution logs unavailable for {ExecutionId}: {Reason}", execution.Id, ex.Message);
                data.Availability = ExecutionLogsAvailability.SourceUnavailable;
                return Success(data);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Reading execution logs failed for {ExecutionId}.", execution.Id);
                data.Availability = ExecutionLogsAvailability.SourceUnavailable;
                return Success(data);
            }

            var nodesById = execution.WorkflowSnapshot?.Nodes?
                .GroupBy(n => n.Id)
                .ToDictionary(g => g.Key, g => g.First())
                ?? new Dictionary<string, NodeEntity>();

            var entries = rawLines
                .Select((raw, index) => (raw, index))
                .Select(x => ExecutionLogLineParser.TryParse(x.raw.Message, out var parsed)
                    ? (x.raw, x.index, parsed)
                    : (x.raw, x.index, parsed: null))
                .Where(x => x.parsed is not null)
                .Select(x =>
                {
                    var source = SourceOf(x.raw.ServiceName);
                    NodeEntity? node = null;
                    if (x.parsed!.NodeId is not null) nodesById.TryGetValue(x.parsed.NodeId, out node);
                    return (x.index, source, entry: new ExecutionLogEntryDto
                    {
                        Timestamp = DateTime.SpecifyKind(x.raw.Timestamp, DateTimeKind.Utc),
                        Level = x.raw.Level,
                        Stage = x.parsed.Stage,
                        NodeId = x.parsed.NodeId,
                        RunIndex = x.parsed.RunIndex,
                        NodeName = node?.Name,
                        NodeType = node?.Type,
                        Message = x.parsed.Text,
                        Source = source,
                    });
                })
                // Api and Worker clocks can differ slightly; ties go api first, then read order.
                .OrderBy(x => x.entry.Timestamp)
                .ThenBy(x => x.source == "api" ? 0 : 1)
                .ThenBy(x => x.index)
                .Select(x => x.entry)
                .ToList();

            if (entries.Count > maxLines)
            {
                data.IsTruncated = true;
                entries = entries.Take(maxLines).ToList();
            }

            data.Logs = entries;
            data.MayStillArrive = MayStillArrive(execution, now, grace);
            return Success(data);
        }

        private static bool MayStillArrive(WorkflowExecutionEntity execution, DateTime now, TimeSpan grace)
        {
            var terminal = execution.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed;
            if (!terminal) return true;
            return execution.FinishedAt.HasValue && now - execution.FinishedAt.Value < grace;
        }

        private static string SourceOf(string serviceName)
            => serviceName.EndsWith("worker", StringComparison.OrdinalIgnoreCase) ? "worker" : "api";

        private static WorkflowExecutionLogsGetResponseDto Success(WorkflowExecutionLogsDto data)
            => new() { IsSuccess = true, Data = data };
    }
}
