using Common.InternalService.Monitor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Read-only uptime monitoring: monitors, their incidents and their response-time / downtime series,
    /// routed as <c>/api/Monitor/{action}</c>. Every action requires a bearer token.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class MonitorController(IMonitorObservabilityService observability) : ControllerBase
    {
        /// <summary><c>GET</c> — the tenant's monitors, optionally narrowed by source type, paged (0-based).</summary>
        [HttpGet, Authorize]
        public Task<PaginatedResponse> GetMonitorList(
            [FromQuery] string tenantId,
            [FromQuery] string? monitorSourcetype,
            [FromQuery] int pageNumber = 0,
            [FromQuery] int pageSize = 10)
            => observability.GetMonitorListAsync(tenantId, monitorSourcetype, pageNumber, pageSize);

        /// <summary><c>GET</c> — the monitors attached to one repository.</summary>
        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorListByRepoId(
            [FromQuery] string tenantId, [FromQuery] string repoId)
            => observability.GetMonitorListByRepoIdAsync(tenantId, repoId);

        /// <summary><c>GET</c> — one monitor by id.</summary>
        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorById([FromQuery] string monitorId)
            => observability.GetMonitorByIdAsync(monitorId);

        //[HttpPost, Authorize]
        //public Task<BaseApiResponse> SaveMonitor([FromBody] SaveMonitorConfigurationRequest request)
        //    => observability.SaveMonitorAsync(request);

        //[HttpPost, Authorize]
        //public Task<BaseApiResponse> UpdateMonitor([FromBody] UpdateMonitorConfigurationRequest request)
        //    => observability.UpdateMonitorAsync(request);

        //[HttpDelete, Authorize]
        //public Task<BaseApiResponse> DeleteMonitor([FromQuery] string itemId)
        //    => observability.DeleteMonitorAsync(itemId);

        /// <summary><c>GET</c> — the incident history for one monitor, paged (0-based).</summary>
        [HttpGet, Authorize]
        public Task<PaginatedResponse> GetIncidentList(
            [FromQuery] string monitorId, int pageNumber = 0, int pageSize = 10)
            => observability.GetIncidentListAsync(monitorId, pageNumber, pageSize);

        /// <summary><c>GET</c> — one monitor with its current status and summary figures.</summary>
        [HttpGet, Authorize]
        public Task<MonitorDetailsResponse> GetMonitorDetails([FromQuery] string monitorId)
            => observability.GetMonitorDetailsAsync(monitorId);

        /// <summary><c>GET</c> — the response-time series for one monitor over an optional date range.</summary>
        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorResponseTime(
            [FromQuery] string monitorId, string? startDate, string? endDate)
            => observability.GetMonitorResponseTimeAsync(monitorId, startDate, endDate);

        /// <summary><c>GET</c> — the downtime series for one monitor over an optional date range.</summary>
        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorDownTime(
            [FromQuery] string monitorId, string? startDate, string? endDate)
            => observability.GetMonitorDownTimeAsync(monitorId, startDate, endDate);

        /// <summary><c>GET</c> — whether the given external service has been configured for monitoring.</summary>
        [HttpGet, Authorize]
        public Task<BaseApiResponse> IsExternalServiceConfigured([FromQuery] string externalServiceId)
            => observability.IsExternalServiceConfiguredAsync(externalServiceId);
    }
}
