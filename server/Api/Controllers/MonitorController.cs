using Common.InternalService.Monitor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class MonitorController(IMonitorObservabilityService observability) : ControllerBase
    {
        [HttpGet, Authorize]
        public Task<PaginatedResponse> GetMonitorList(
            [FromQuery] string projectKey,
            [FromQuery] string? monitorSourcetype,
            [FromQuery] int pageNumber = 0,
            [FromQuery] int pageSize = 10)
            => observability.GetMonitorListAsync(projectKey, monitorSourcetype, pageNumber, pageSize);

        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorListByRepoId(
            [FromQuery] string projectKey, [FromQuery] string repoId)
            => observability.GetMonitorListByRepoIdAsync(projectKey, repoId);

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

        [HttpGet, Authorize]
        public Task<PaginatedResponse> GetIncidentList(
            [FromQuery] string monitorId, int pageNumber = 0, int pageSize = 10)
            => observability.GetIncidentListAsync(monitorId, pageNumber, pageSize);

        [HttpGet, Authorize]
        public Task<MonitorDetailsResponse> GetMonitorDetails([FromQuery] string monitorId)
            => observability.GetMonitorDetailsAsync(monitorId);

        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorResponseTime(
            [FromQuery] string monitorId, string? startDate, string? endDate)
            => observability.GetMonitorResponseTimeAsync(monitorId, startDate, endDate);

        [HttpGet, Authorize]
        public Task<BaseApiResponse> GetMonitorDownTime(
            [FromQuery] string monitorId, string? startDate, string? endDate)
            => observability.GetMonitorDownTimeAsync(monitorId, startDate, endDate);

        [HttpGet, Authorize]
        public Task<BaseApiResponse> IsExternalServiceConfigured([FromQuery] string externalServiceId)
            => observability.IsExternalServiceConfiguredAsync(externalServiceId);
    }
}
