using Proxy.DomainService.Dtos;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Read-only projections over the <c>ProxyExecutions</c> rows Phase 2 writes: the <em>Request logs</em>
    /// list and expanded row, the Overview tiles, and the CSV export (SPEC3 &sect;3). Nothing here writes.
    /// All "24 h" figures use a rolling window computed per request from an injected clock.
    /// </summary>
    public interface IProxyExecutionService
    {
        /// <summary>SPEC &sect;3.1 &mdash; filtered, paged, newest-first log list (or Live tail via <c>afterId</c>).</summary>
        Task<ProxyGetExecutionsResponseDto> GetExecutionsAsync(string tenantId, ProxyGetExecutionsRequestDto request);

        /// <summary>SPEC &sect;3.2 &mdash; one expanded row with the display-clipped response body; <c>data:null</c> on any mismatch.</summary>
        Task<ProxyGetExecutionResponseDto> GetExecutionAsync(string tenantId, ProxyGetExecutionRequestDto request);

        /// <summary>SPEC &sect;3.3 &mdash; the rolling 24 h tiles plus the Configuration-panel convenience fields.</summary>
        Task<ProxyGetOverviewResponseDto> GetOverviewAsync(string tenantId, ProxyGetOverviewRequestDto request);

        /// <summary>SPEC &sect;3.4 &mdash; the filtered last-24 h log as an RFC 4180 CSV, capped at 50,000 rows.</summary>
        Task<ProxyCsvExportResult> ExportExecutionsCsvAsync(string tenantId, ProxyExportExecutionsRequestDto request);
    }

    /// <summary>
    /// Outcome of <see cref="IProxyExecutionService.ExportExecutionsCsvAsync"/>. On success the controller
    /// streams <see cref="Content"/> as <c>text/csv</c> with <see cref="FileName"/> and, when
    /// <see cref="Truncated"/>, the <c>X-Proxy-Export-Truncated: true</c> header. On failure it returns
    /// <see cref="HttpStatus"/> with <see cref="Code"/> / <see cref="Message"/> / <see cref="Errors"/>.
    /// </summary>
    public sealed class ProxyCsvExportResult
    {
        public bool IsSuccess { get; private init; }

        public int HttpStatus { get; private init; } = 200;

        public byte[] Content { get; private init; } = Array.Empty<byte>();

        public string FileName { get; private init; } = string.Empty;

        public bool Truncated { get; private init; }

        public string? Code { get; private init; }

        public string? Message { get; private init; }

        public IDictionary<string, string>? Errors { get; private init; }

        public static ProxyCsvExportResult Ok(byte[] content, string fileName, bool truncated) => new()
        {
            IsSuccess = true,
            HttpStatus = 200,
            Content = content,
            FileName = fileName,
            Truncated = truncated,
        };

        public static ProxyCsvExportResult Failure(
            int httpStatus, string code, string message, IDictionary<string, string>? errors = null) => new()
        {
            IsSuccess = false,
            HttpStatus = httpStatus,
            Code = code,
            Message = message,
            Errors = errors,
        };
    }
}
