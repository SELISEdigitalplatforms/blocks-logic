using Blocks.Genesis.Entities.Logic;
using Microsoft.Extensions.Logging;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Repositories;

namespace Scheduler.DomainService.Services
{
    public class ScheduleExecutionQueryService : IScheduleExecutionQueryService
    {
        internal const string Succeeded = "Succeeded";
        internal const string Failed = "Failed";

        /// <summary>Above this many distinct failure codes the <c>$in</c> list gives way to a range (and an in-memory sort).</summary>
        internal const int MaxFailureStatuses = 150;

        /// <summary>Every 2xx code: the <c>$in</c> list for the Succeeded filter.</summary>
        internal static readonly IReadOnlyCollection<int> SuccessStatuses = Enumerable.Range(200, 100).ToArray();

        private readonly IScheduleRepository _scheduleRepository;
        private readonly IScheduleExecutionRepository _executionRepository;
        private readonly ILogger<ScheduleExecutionQueryService> _logger;
        private int _failureFallbackLogged;

        public ScheduleExecutionQueryService(
            IScheduleRepository scheduleRepository,
            IScheduleExecutionRepository executionRepository,
            ILogger<ScheduleExecutionQueryService> logger)
        {
            _scheduleRepository = scheduleRepository;
            _executionRepository = executionRepository;
            _logger = logger;
        }

        private const string NotLoaded = "(not loaded)";

        public async Task<GetScheduleExecutionsResponseDto> GetScheduleExecutionsAsync(GetScheduleExecutionsRequestDto request)
        {
            // Logs carry the id as stored, once the schedule is loaded, never the raw request value (log forging).
            string? scheduleId = null;
            try
            {
                scheduleId = await LoadApplicationScheduleIdAsync(request.ScheduleId);
                if (scheduleId is null)
                {
                    return new GetScheduleExecutionsResponseDto { Errors = NotFound("Schedule not found") };
                }

                IReadOnlyCollection<int>? statuses = null;
                var nonSuccessRange = false;
                if (request.Outcome == Succeeded)
                {
                    statuses = SuccessStatuses;
                }
                else if (request.Outcome == Failed)
                {
                    statuses = await FailureStatusesAsync(scheduleId);
                    if (statuses is null)
                    {
                        nonSuccessRange = true;
                    }
                    else if (statuses.Count == 0)
                    {
                        // Never failed: nothing can match, so no row query runs.
                        return new GetScheduleExecutionsResponseDto { IsSuccess = true, Data = [], TotalCount = 0 };
                    }
                }

                var filter = new ScheduleExecutionFilter(
                    scheduleId,
                    statuses,
                    request.StartDate is { } start ? GetScheduleExecutionsRequestDto.ToUtc(start) : null,
                    request.EndDate is { } end ? GetScheduleExecutionsRequestDto.ToUtc(end) : null,
                    nonSuccessRange);

                var totalCount = await _executionRepository.CountAsync(filter);
                var rows = await _executionRepository.GetPageAsync(filter, request.PageNumber, request.PageSize);

                return new GetScheduleExecutionsResponseDto
                {
                    IsSuccess = true,
                    Data = rows.Select(ToListItem).ToList(),
                    TotalCount = totalCount,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list executions of schedule {ScheduleId}.", scheduleId ?? NotLoaded);
                return new GetScheduleExecutionsResponseDto { Errors = SomethingWentWrong() };
            }
        }

        public async Task<GetScheduleExecutionResponseDto> GetScheduleExecutionAsync(GetScheduleExecutionRequestDto request)
        {
            // As above: only stored ids reach the log, never the request's values.
            string? scheduleId = null;
            try
            {
                scheduleId = await LoadApplicationScheduleIdAsync(request.ScheduleId);
                if (scheduleId is null)
                {
                    return new GetScheduleExecutionResponseDto { Errors = NotFound("Schedule not found") };
                }

                var row = await _executionRepository.GetByIdAsync(scheduleId, request.ExecutionId);
                if (row is null)
                {
                    return new GetScheduleExecutionResponseDto { Errors = NotFound("Execution not found") };
                }

                return new GetScheduleExecutionResponseDto { IsSuccess = true, Data = ToDetail(row) };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read an execution of schedule {ScheduleId}.", scheduleId ?? NotLoaded);
                return new GetScheduleExecutionResponseDto { Errors = SomethingWentWrong() };
            }
        }

        /// <summary>
        /// The schedule guard: the same not-found answer (null) for a missing, denied, Internal or other-tenant
        /// schedule (the read goes to the caller's own DB through data access). Returns the id as stored.
        /// </summary>
        private async Task<string?> LoadApplicationScheduleIdAsync(string scheduleId)
        {
            var schedule = await _scheduleRepository.GetByIdAsync(scheduleId);
            return schedule is not null && schedule.Kind == ScheduleKind.Application ? schedule.ItemId : null;
        }

        /// <summary>
        /// The non-2xx codes this schedule has ever stored (over the whole schedule, not the date range, so the
        /// distinct stays index-only). Null when there are more than <see cref="MaxFailureStatuses"/>: the caller
        /// then falls back to a range.
        /// </summary>
        internal async Task<IReadOnlyCollection<int>?> FailureStatusesAsync(string scheduleId)
        {
            var failures = (await _executionRepository.GetDistinctStatusesAsync(scheduleId))
                .Where(status => !IsSuccess(status))
                .Distinct()
                .OrderBy(status => status)
                .ToList();

            if (failures.Count <= MaxFailureStatuses)
            {
                return failures;
            }

            if (Interlocked.Exchange(ref _failureFallbackLogged, 1) == 0)
            {
                _logger.LogWarning(
                    "Schedule {ScheduleId} has {Count} distinct failure statuses; the Failed filter falls back to a status range and an in-memory sort.",
                    scheduleId, failures.Count);
            }

            return null;
        }

        internal static bool IsSuccess(int status) => status is >= 200 and <= 299;

        internal static int? StatusOf(ScheduleExecution row)
        {
            if (row.Response is null || !row.Response.TryGetValue(ScheduleExecution.ResponseStatusKey, out var value))
            {
                return null;
            }

            return value switch
            {
                int status => status,
                long status when status is >= int.MinValue and <= int.MaxValue => (int)status,
                _ => null,
            };
        }

        internal static string? BodyOf(ScheduleExecution row)
        {
            if (row.Response is null || !row.Response.TryGetValue(ScheduleExecution.ResponseBodyKey, out var value))
            {
                return null;
            }

            return value as string ?? value?.ToString();
        }

        internal static long? DurationOf(ScheduleExecution row) =>
            row.FiredUtc is { } fired && row.FinishedUtc is { } finished
                ? (long)Math.Round((finished - fired).TotalMilliseconds)
                : null;

        internal static string OutcomeOf(int? status) => status is { } code && IsSuccess(code) ? Succeeded : Failed;

        internal static ScheduleExecutionListItemDto ToListItem(ScheduleExecution row)
        {
            var status = StatusOf(row);
            return new ScheduleExecutionListItemDto
            {
                Id = row.ItemId,
                FiredUtc = row.FiredUtc,
                FinishedUtc = row.FinishedUtc,
                DurationMs = DurationOf(row),
                Status = status,
                Outcome = OutcomeOf(status),
            };
        }

        internal static ScheduleExecutionDto ToDetail(ScheduleExecution row)
        {
            var status = StatusOf(row);
            return new ScheduleExecutionDto
            {
                Id = row.ItemId,
                ScheduleId = row.ScheduleId,
                ScheduleName = row.ScheduleName,
                FiredUtc = row.FiredUtc,
                FinishedUtc = row.FinishedUtc,
                DurationMs = DurationOf(row),
                Outcome = OutcomeOf(status),
                Status = status,
                Body = BodyOf(row),
                Snapshot = row.Snapshot,
            };
        }

        private static Dictionary<string, string> NotFound(string message) => new() { ["not_found"] = message };

        private static Dictionary<string, string> SomethingWentWrong() => new() { ["Message"] = "Something went wrong" };
    }
}
