using System.Text;
using Microsoft.Extensions.Logging;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Read-only implementation of <see cref="IProxyExecutionService"/>. Priority tradeoff (SPEC3 &sect;1):
    /// cheap, bounded queries over real-time precision &mdash; every figure is a rolling 24 h window computed
    /// on demand, result sizes are capped, and "Live" is a short-poll tail, not a push channel. The window
    /// start comes from an injected <see cref="TimeProvider"/> so tests can pin the clock.
    /// </summary>
    public sealed class ProxyExecutionService : IProxyExecutionService
    {
        /// <summary>Rolling window for every "24h" figure (SPEC &sect;3).</summary>
        internal static readonly TimeSpan Window = TimeSpan.FromHours(24);

        /// <summary>Max <c>pageSize</c> accepted by <c>GetExecutions</c> (SPEC &sect;3 / C1).</summary>
        internal const int MaxPageSize = 200;

        /// <summary>Transport guard for <c>GetExecution.responseBody</c> (SPEC &sect;3.2 / C6). The stored row is untouched.</summary>
        internal const int ResponseBodyDisplayLimitBytes = 64 * 1024;

        /// <summary>Hard row cap for the CSV export (SPEC &sect;3.4 / C5).</summary>
        internal const int ExportRowCap = 50_000;

        private readonly IProxyExecutionRepository _executionRepository;
        private readonly IProxyRepository _proxyRepository;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ProxyExecutionService> _logger;

        public ProxyExecutionService(
            IProxyExecutionRepository executionRepository,
            IProxyRepository proxyRepository,
            TimeProvider timeProvider,
            ILogger<ProxyExecutionService> logger)
        {
            _executionRepository = executionRepository;
            _proxyRepository = proxyRepository;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<ProxyGetExecutionsResponseDto> GetExecutionsAsync(
            string tenantId, ProxyGetExecutionsRequestDto request)
        {
            var proxyId = (request.ProxyId ?? string.Empty).Trim();
            var pageSize = request.PageSize;
            var pageNumber = request.PageNumber;

            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (proxyId.Length == 0)
            {
                errors["proxyId"] = "proxyId is required.";
            }

            if (!ProxyStatusClassParser.TryParse(request.StatusClass, out var statusClass))
            {
                errors["statusClass"] = ProxyStatusClassParser.InvalidMessage;
            }

            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                errors["pageSize"] = $"pageSize must be between 1 and {MaxPageSize}.";
            }

            if (pageNumber < 0)
            {
                errors["pageNumber"] = "pageNumber must be 0 or greater.";
            }

            if (errors.Count > 0)
            {
                _logger.LogWarning(
                    "GetExecutions for tenant {TenantId} rejected: {Errors}", tenantId, string.Join("; ", errors.Values));
                return new ProxyGetExecutionsResponseDto
                {
                    Data = new List<ProxyExecutionListItemDto>(),
                    TotalCount = 0,
                    HttpStatus = 400,
                    Code = ProxyErrorCodes.Validation,
                    Message = "The request is invalid.",
                    Errors = errors,
                };
            }

            if (!await ProxyExistsOrHasRowsAsync(tenantId, proxyId))
            {
                _logger.LogInformation(
                    "GetExecutions for tenant {TenantId}: proxy {ProxyId} is unknown and has no rows; 404.", tenantId, proxyId);
                return NotFoundList(proxyId);
            }

            var since = WindowStart();
            var asOf = ResolveAsOf(request.AsOfUtc, since);
            var totalCount = await _executionRepository.CountAsync(tenantId, proxyId, statusClass, since, asOf);

            List<ProxyExecutionEntity> rows;
            var tail = await ResolveTailCursorAsync(tenantId, proxyId, request.AfterId, since);
            if (tail is { } cursor)
            {
                rows = await _executionRepository.GetNewerThanAsync(
                    tenantId, proxyId, statusClass, since, cursor.StartedAtUtc, cursor.ItemId, pageSize);
            }
            else
            {
                // No afterId, or an unknown / out-of-window afterId (C8): serve the newest page. pageNumber is
                // ignored whenever afterId was supplied at all (H3).
                var effectivePage = string.IsNullOrWhiteSpace(request.AfterId) ? pageNumber : 0;
                rows = await _executionRepository.GetPageAsync(
                    tenantId, proxyId, statusClass, since, asOf, pageSize, effectivePage);
            }

            _logger.LogInformation(
                "GetExecutions for tenant {TenantId} proxy {ProxyId}: returned {Returned} of {TotalCount} (statusClass {StatusClass}, afterId {AfterId}).",
                tenantId, proxyId, rows.Count, totalCount, statusClass, request.AfterId ?? "(none)");

            return new ProxyGetExecutionsResponseDto
            {
                Data = rows.Select(ToListItem).ToList(),
                TotalCount = totalCount,
                AsOfUtc = asOf,
            };
        }

        public async Task<ProxyGetExecutionResponseDto> GetExecutionAsync(
            string tenantId, ProxyGetExecutionRequestDto request)
        {
            var itemId = (request.ItemId ?? string.Empty).Trim();
            var proxyId = (request.ProxyId ?? string.Empty).Trim();

            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (itemId.Length == 0)
            {
                errors["itemId"] = "itemId is required.";
            }

            if (proxyId.Length == 0)
            {
                errors["proxyId"] = "proxyId is required.";
            }

            if (errors.Count > 0)
            {
                _logger.LogWarning(
                    "GetExecution for tenant {TenantId} rejected: {Errors}", tenantId, string.Join("; ", errors.Values));
                return new ProxyGetExecutionResponseDto
                {
                    Data = null,
                    HttpStatus = 400,
                    Code = ProxyErrorCodes.Validation,
                    Message = "The request is invalid.",
                    Errors = errors,
                };
            }

            var row = await _executionRepository.GetByIdAsync(tenantId, proxyId, itemId);
            if (row is null)
            {
                _logger.LogInformation(
                    "GetExecution for tenant {TenantId}: no row for itemId {ItemId} + proxyId {ProxyId}; data:null.",
                    tenantId, itemId, proxyId);
                return new ProxyGetExecutionResponseDto { Data = null };
            }

            var (body, truncated) = ClipForDisplay(row.ResponseBody);

            _logger.LogInformation(
                "GetExecution for tenant {TenantId}: returned itemId {ItemId} (status {StatusCode}, body clipped: {Truncated}).",
                tenantId, itemId, row.StatusCode, truncated);

            return new ProxyGetExecutionResponseDto
            {
                Data = new ProxyExecutionDetailDto
                {
                    ItemId = row.ItemId,
                    ProxyId = row.ProxyId,
                    ProxySlug = row.ProxySlug,
                    StartedAtUtc = row.StartedAtUtc,
                    FinishedAtUtc = row.FinishedAtUtc,
                    LatencyMs = row.LatencyMs,
                    RequestMethod = row.RequestMethod,
                    RequestPath = row.RequestPath,
                    RequestQuery = row.RequestQuery,
                    UpstreamUrl = row.UpstreamUrl,
                    UpstreamHost = row.UpstreamHost,
                    InjectedHeaderKeys = new List<string>(row.InjectedHeaderKeys),
                    InjectedQueryKeys = new List<string>(row.InjectedQueryKeys),
                    StatusCode = row.StatusCode,
                    UpstreamStatusCode = row.UpstreamStatusCode,
                    Outcome = row.Outcome,
                    ErrorMessage = row.ErrorMessage,
                    ResponseContentType = row.ResponseContentType,
                    ResponseFilterApplied = row.ResponseFilterApplied,
                    ResponseFilterNote = row.ResponseFilterNote,
                    ResponseBodyBytes = row.ResponseBodyBytes,
                    ResponseBody = body,
                    ResponseBodyTruncatedForDisplay = truncated,
                },
            };
        }

        public async Task<ProxyGetOverviewResponseDto> GetOverviewAsync(
            string tenantId, ProxyGetOverviewRequestDto request)
        {
            var proxyId = (request.ProxyId ?? string.Empty).Trim();
            if (proxyId.Length == 0)
            {
                _logger.LogWarning("GetOverview for tenant {TenantId} rejected: proxyId is required.", tenantId);
                return new ProxyGetOverviewResponseDto
                {
                    Data = null,
                    HttpStatus = 400,
                    Code = ProxyErrorCodes.Validation,
                    Message = "The request is invalid.",
                    Errors = new Dictionary<string, string>(StringComparer.Ordinal) { ["proxyId"] = "proxyId is required." },
                };
            }

            var proxy = await _proxyRepository.GetAsync(tenantId, proxyId);
            if (proxy is null && !await _executionRepository.AnyForProxyAsync(tenantId, proxyId))
            {
                _logger.LogInformation(
                    "GetOverview for tenant {TenantId}: proxy {ProxyId} is unknown and has no rows; 404.", tenantId, proxyId);
                return new ProxyGetOverviewResponseDto
                {
                    Data = null,
                    HttpStatus = 404,
                    Code = ProxyErrorCodes.NotFound,
                    Message = $"Proxy '{proxyId}' was not found.",
                };
            }

            var since = WindowStart();
            var stats = await _executionRepository.GetStatsAsync(tenantId, proxyId, since);

            var calls = stats.Count;
            var avgLatency = calls > 0 ? (int)Math.Round(stats.AvgLatencyMs, MidpointRounding.AwayFromZero) : 0;
            var errorRatePct = calls > 0
                ? Math.Round(stats.ErrorCount * 100d / calls, 1, MidpointRounding.AwayFromZero)
                : 0d;

            var dto = new ProxyOverviewDto
            {
                Calls24h = calls,
                AvgLatencyMs = avgLatency,
                ErrorRatePct = errorRatePct,
                ErrorRateIsHigh = errorRatePct > 5,
                CredentialRefs = CredentialRefsOf(proxy),
                Methods = proxy is null ? new List<string>() : proxy.Methods.Select(m => m.Wire()).ToList(),
                LastCallAtUtc = calls > 0 ? stats.LastCallAtUtc : null,
            };

            _logger.LogInformation(
                "GetOverview for tenant {TenantId} proxy {ProxyId}: calls24h {Calls}, avgLatencyMs {Avg}, errorRatePct {ErrorRate}.",
                tenantId, proxyId, dto.Calls24h, dto.AvgLatencyMs, dto.ErrorRatePct);

            return new ProxyGetOverviewResponseDto { Data = dto };
        }

        public async Task<ProxyCsvExportResult> ExportExecutionsCsvAsync(
            string tenantId, ProxyExportExecutionsRequestDto request)
        {
            var proxyId = (request.ProxyId ?? string.Empty).Trim();

            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (proxyId.Length == 0)
            {
                errors["proxyId"] = "proxyId is required.";
            }

            if (!ProxyStatusClassParser.TryParse(request.StatusClass, out var statusClass))
            {
                errors["statusClass"] = ProxyStatusClassParser.InvalidMessage;
            }

            if (errors.Count > 0)
            {
                _logger.LogWarning(
                    "ExportExecutionsCsv for tenant {TenantId} rejected: {Errors}", tenantId, string.Join("; ", errors.Values));
                return ProxyCsvExportResult.Failure(400, ProxyErrorCodes.Validation, "The request is invalid.", errors);
            }

            var proxy = await _proxyRepository.GetAsync(tenantId, proxyId);
            if (proxy is null && !await _executionRepository.AnyForProxyAsync(tenantId, proxyId))
            {
                _logger.LogInformation(
                    "ExportExecutionsCsv for tenant {TenantId}: proxy {ProxyId} is unknown and has no rows; 404.", tenantId, proxyId);
                return ProxyCsvExportResult.Failure(404, ProxyErrorCodes.NotFound, $"Proxy '{proxyId}' was not found.");
            }

            var since = WindowStart();

            // The export is a single shot, so there is no paging session to pin: "now" as the upper bound is
            // the same set of rows an unbounded count would see (no row is written with a future timestamp),
            // and it keeps the count aligned with the rows the export itself reads.
            var matched = await _executionRepository.CountAsync(
                tenantId, proxyId, statusClass, since, _timeProvider.GetUtcNow().UtcDateTime);
            var rows = await _executionRepository.GetForExportAsync(tenantId, proxyId, statusClass, since, ExportRowCap);
            var truncated = matched > ExportRowCap;

            var slug = !string.IsNullOrEmpty(proxy?.Slug)
                ? proxy!.Slug
                : rows.FirstOrDefault()?.ProxySlug is { Length: > 0 } rowSlug
                    ? rowSlug
                    : proxyId;
            var fileName = $"proxy-{slug}-logs-{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHHmmss}.csv";

            var content = ProxyCsvWriter.Write(rows);

            _logger.LogInformation(
                "ExportExecutionsCsv for tenant {TenantId} proxy {ProxyId}: wrote {Rows} of {Matched} rows (truncated: {Truncated}), {Bytes} bytes.",
                tenantId, proxyId, rows.Count, matched, truncated, content.Length);

            return ProxyCsvExportResult.Ok(content, fileName, truncated);
        }

        private DateTime WindowStart() => _timeProvider.GetUtcNow().UtcDateTime - Window;

        /// <summary>
        /// The upper bound for a paging session: the caller's <c>asOfUtc</c> when it is usable, otherwise now.
        /// A value in the future would let newly written rows leak in and reintroduce the drift it exists to
        /// prevent; one older than the window start would return nothing at all. Both are clamped rather than
        /// rejected — a stale pin from a tab left open overnight should quietly behave like a refresh, not 400.
        /// </summary>
        private DateTime ResolveAsOf(DateTime? requested, DateTime windowStart)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (requested is not { } asOf)
            {
                return now;
            }

            if (asOf.Kind != DateTimeKind.Utc)
            {
                asOf = asOf.Kind == DateTimeKind.Local ? asOf.ToUniversalTime() : DateTime.SpecifyKind(asOf, DateTimeKind.Utc);
            }

            if (asOf > now) return now;
            return asOf < windowStart ? now : asOf;
        }

        private async Task<bool> ProxyExistsOrHasRowsAsync(string tenantId, string proxyId)
        {
            var proxy = await _proxyRepository.GetAsync(tenantId, proxyId);
            return proxy is not null || await _executionRepository.AnyForProxyAsync(tenantId, proxyId);
        }

        /// <summary>
        /// Resolves the <c>afterId</c> cursor. Returns <c>null</c> when there is no <c>afterId</c>, the id is
        /// unknown for the tenant, it belongs to another proxy, or it started before the window (C8) &mdash;
        /// in every one of those cases the caller falls back to the newest page.
        /// </summary>
        private async Task<TailCursor?> ResolveTailCursorAsync(
            string tenantId, string proxyId, string? afterId, DateTime since)
        {
            if (string.IsNullOrWhiteSpace(afterId))
            {
                return null;
            }

            var reference = await _executionRepository.FindByItemIdAsync(tenantId, afterId.Trim());
            if (reference is null
                || !string.Equals(reference.ProxyId, proxyId, StringComparison.Ordinal)
                || reference.StartedAtUtc < since)
            {
                _logger.LogInformation(
                    "GetExecutions for tenant {TenantId} proxy {ProxyId}: afterId {AfterId} is unknown or out of window; ignoring it.",
                    tenantId, proxyId, afterId);
                return null;
            }

            return new TailCursor(reference.StartedAtUtc, reference.ItemId);
        }

        private static (string? Body, bool Truncated) ClipForDisplay(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return (body, false);
            }

            var bytes = Encoding.UTF8.GetByteCount(body);
            if (bytes <= ResponseBodyDisplayLimitBytes)
            {
                return (body, false);
            }

            var raw = Encoding.UTF8.GetBytes(body);
            var clipped = Encoding.UTF8.GetString(raw, 0, ResponseBodyDisplayLimitBytes);
            return (clipped, true);
        }

        private static List<string> CredentialRefsOf(ProxyDetailEntity? proxy)
        {
            if (proxy is null)
            {
                return new List<string>();
            }

            return proxy.Headers.Concat(proxy.Query).Concat(proxy.BodyMerge)
                .SelectMany(kv => ProxyVarRef.Tokens(kv.Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static ProxyExecutionListItemDto ToListItem(ProxyExecutionEntity row) => new()
        {
            ItemId = row.ItemId,
            StartedAtUtc = row.StartedAtUtc,
            RequestMethod = row.RequestMethod,
            RequestPath = row.RequestPath,
            StatusCode = row.StatusCode,
            LatencyMs = row.LatencyMs,
            Outcome = row.Outcome,
            UpstreamHost = row.UpstreamHost,
        };

        private static ProxyGetExecutionsResponseDto NotFoundList(string proxyId) => new()
        {
            Data = new List<ProxyExecutionListItemDto>(),
            TotalCount = 0,
            HttpStatus = 404,
            Code = ProxyErrorCodes.NotFound,
            Message = $"Proxy '{proxyId}' was not found.",
        };

        private readonly record struct TailCursor(DateTime StartedAtUtc, string ItemId);
    }
}
