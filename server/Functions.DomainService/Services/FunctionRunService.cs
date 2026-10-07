using Blocks.Genesis;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    public interface IFunctionRunService
    {
        Task<(IReadOnlyList<RunSummaryDto> Items, long TotalCount)> GetAllAsync(
            string tenantId, GetRunsRequestDto request, CancellationToken cancellationToken = default);

        Task<RunDetailDto> GetAsync(string tenantId, string runId, CancellationToken cancellationToken = default);

        /// <summary>
        /// What an anonymous poller may see of a run: the same shape as the 200 from a waiting
        /// invoke — status, result and error — and nothing else. Never the input, the headers it
        /// carried, logs, output-action results or who invoked it. The caller has already proved
        /// its poll token; this only reads.
        /// </summary>
        Task<InvokeResultDto> GetPollResultAsync(string tenantId, string runId, CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<RunLogLineDto> Items, long TotalCount)> GetLogsAsync(
            string tenantId, string runId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests cancellation. Best-effort and asynchronous: setting the Redis key only asks
        /// the runner to stop; the run's status becomes <c>CANCELLED</c> once the runner
        /// actually acts on it (typically within a third of the lease interval — see
        /// <c>verify/scenarios/cancel.sh</c> on the Runner VM side, which measures this at a
        /// few seconds in practice). Calling this on a run that has already finished is a no-op.
        /// </summary>
        Task CancelAsync(
            string tenantId, string runId, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        Task<InvokeResultDto> ReplayAsync(
            string tenantId, string runId, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);
    }

    public class FunctionRunService : IFunctionRunService
    {
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionRunLogRepository _logRepository;
        private readonly IFunctionAuditService _auditService;
        private readonly IFunctionInvocationService _invocationService;
        private readonly ICacheClient _cache;
        private readonly ILogger<FunctionRunService> _logger;

        public FunctionRunService(
            IFunctionRunRepository runRepository,
            IFunctionRunLogRepository logRepository,
            IFunctionAuditService auditService,
            IFunctionInvocationService invocationService,
            ICacheClient cache,
            ILogger<FunctionRunService> logger)
        {
            _runRepository = runRepository;
            _logRepository = logRepository;
            _auditService = auditService;
            _invocationService = invocationService;
            _cache = cache;
            _logger = logger;
        }

        public async Task<(IReadOnlyList<RunSummaryDto> Items, long TotalCount)> GetAllAsync(
            string tenantId, GetRunsRequestDto request, CancellationToken cancellationToken = default)
        {
            // "Running" is the design's chip for "still going", which is every non-terminal
            // status — Queued, Claimed, Starting, Running, OutputProcessing. Filtering it here
            // rather than in the client keeps the page, the total and the pager in agreement.
            var activeOnly = string.Equals(request.Status, "Running", StringComparison.OrdinalIgnoreCase);

            RunStatus? status = null;
            if (!activeOnly &&
                !string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<RunStatus>(request.Status, ignoreCase: true, out var parsed))
            {
                status = parsed;
            }

            InvokedByType? invokedBy = null;
            if (!string.IsNullOrWhiteSpace(request.InvokedBy) &&
                Enum.TryParse<InvokedByType>(request.InvokedBy, ignoreCase: true, out var parsedInvokedBy))
            {
                invokedBy = parsedInvokedBy;
            }

            var filter = new FunctionRunFilter(
                request.FunctionId, status, request.FromUtc, request.ToUtc, invokedBy,
                request.SearchKey, activeOnly);
            var (items, totalCount) = await _runRepository.GetAllAsync(
                tenantId, filter, request.PageNumber, request.PageSize, cancellationToken);

            return (items.Select(RunSummaryDto.From).ToList(), totalCount);
        }

        public async Task<RunDetailDto> GetAsync(string tenantId, string runId, CancellationToken cancellationToken = default)
        {
            var run = await GetEntityAsync(tenantId, runId, cancellationToken);
            var dto = RunDetailDto.From(run);
            if (!FunctionWireMapping.IsTerminal(run.Status))
            {
                var (live, phase) = await ReadLiveStatusAsync(runId);
                dto.Status = LiveStatus(run.Status, live, phase).ToString();
            }
            return dto;
        }

        /// <summary>
        /// The runner writes a run's progress (Claimed, Starting, Running) only to the run's Redis
        /// hash; the record says Queued until the result is recorded. Without this a watcher saw
        /// Queued for the whole build and run, then a sudden finish. Null when Redis is unreadable
        /// or the hash is gone — the record's status is shown, as before.
        /// </summary>
        private async Task<(string? Status, string? Phase)> ReadLiveStatusAsync(string runId)
        {
            try
            {
                var values = await _cache.CacheDatabase().HashGetAsync(
                    FunctionQueueKeys.Run(runId), [(RedisValue)"status", (RedisValue)FunctionQueueKeys.RunPhaseField]);
                return (values[0].IsNullOrEmpty ? null : values[0].ToString(),
                        values[1].IsNullOrEmpty ? null : values[1].ToString());
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                return (null, null);
            }
        }

        /// <summary>
        /// The status to show for a run the record still has open: the runner's live one when it
        /// is further along (a test's build phase counts as Claimed). Never terminal — an outcome is shown only once it is recorded, with
        /// its result and error — and never backwards.
        /// </summary>
        internal static RunStatus LiveStatus(RunStatus recorded, string? liveWire, string? phase = null)
        {
            if (FunctionWireMapping.IsTerminal(recorded)) return recorded;
            var live = FunctionWireMapping.ToRunStatus(liveWire, out var recognised);
            if (!recognised) live = RunStatus.Queued;
            // A test building its image on a runner: shown as Claimed (taken, being prepared).
            if (live == RunStatus.Queued && phase == FunctionQueueKeys.RunPhaseBuilding) live = RunStatus.Claimed;
            // Finished on the runner, not yet recorded: still "running" from the watcher's side.
            if (FunctionWireMapping.IsTerminal(live)) live = RunStatus.Running;
            return live > recorded ? live : recorded;
        }

        public async Task<InvokeResultDto> GetPollResultAsync(
            string tenantId, string runId, CancellationToken cancellationToken = default)
        {
            var run = await GetEntityAsync(tenantId, runId, cancellationToken);
            var terminal = FunctionWireMapping.IsTerminal(run.Status);
            return new InvokeResultDto
            {
                RunId = run.ItemId,
                Status = FunctionWireMapping.ToWire(run.Status),
                // Only once there is an outcome, exactly like WaitForResultAsync's 200 shape.
                Result = terminal ? run.Result : null,
                ErrorCode = terminal && run.ErrorCode != RunErrorCode.None ? run.ErrorCode.ToString() : null,
                ErrorMessage = terminal ? run.ErrorMessage : null,
            };
        }

        public async Task<(IReadOnlyList<RunLogLineDto> Items, long TotalCount)> GetLogsAsync(
            string tenantId, string runId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var (items, totalCount) = await _logRepository.GetByRunAsync(tenantId, runId, pageNumber, pageSize, cancellationToken);

            var dtos = items.Select(log => new RunLogLineDto
            {
                Seq = log.Seq,
                Timestamp = log.Timestamp,
                Level = log.Level,
                Message = log.Message,
                Data = log.Data,
            }).ToList();

            return (dtos, totalCount);
        }

        public async Task CancelAsync(
            string tenantId, string runId, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            var run = await GetEntityAsync(tenantId, runId, cancellationToken);
            if (FunctionWireMapping.IsTerminal(run.Status))
            {
                _logger.LogInformation("Ignoring cancel for run {RunId}: already {Status}", runId, run.Status);
                return;
            }

            await _cache.CacheDatabase().StringSetAsync(
                FunctionQueueKeys.Cancel(runId), "1", FunctionQueueKeys.CancelTtl);

            await _auditService.RecordAsync(
                tenantId, run.FunctionId, FunctionsConstants.AuditActions.RunCancelled, actorId, actorEmail,
                new { RunId = runId }, cancellationToken);
        }

        public async Task<InvokeResultDto> ReplayAsync(
            string tenantId, string runId, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            var original = await GetEntityAsync(tenantId, runId, cancellationToken);

            var result = await _invocationService.ReplayAsync(tenantId, original, cancellationToken);

            await _auditService.RecordAsync(
                tenantId, original.FunctionId, FunctionsConstants.AuditActions.RunReplayed, actorId, actorEmail,
                new { OriginalRunId = runId, NewRunId = result.RunId }, cancellationToken);

            return result;
        }

        private async Task<FunctionRunEntity> GetEntityAsync(
            string tenantId, string runId, CancellationToken cancellationToken)
        {
            var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
            return run ?? throw new FunctionNotFoundException($"run '{runId}' was not found");
        }
    }
}
