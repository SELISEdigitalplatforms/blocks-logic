using DomainService.Health.Models;
using DomainService.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObservabilityDriver;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Health-check configuration and the heartbeat endpoint monitored services call, routed as
    /// <c>/api/Health/{action}</c>.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class HealthController(IObservabilityDriverService observability) : ControllerBase
    {
        /// <summary><c>POST</c> — creates a health-check configuration.</summary>
        [HttpPost, Authorize]
        public Task<BaseApiResponse> SaveHealth([FromBody] SaveHealthConfigurationRequest request)
            => observability.SaveHealthAsync(request);

        /// <summary><c>POST</c> — updates an existing health-check configuration.</summary>
        [HttpPost, Authorize]
        public Task<BaseApiResponse> UpdateHealth([FromBody] UpdateHealthConfigurationRequest request)
            => observability.UpdateHealthAsync(request);

        /// <summary>
        /// <c>GET /api/Health/Ping/{itemId}</c> — the heartbeat a monitored service calls to report itself
        /// alive. Deliberately unauthenticated: the caller is an external service holding only the id.
        /// </summary>
        [HttpGet("{itemId}")]
        public async Task<IActionResult> Ping([FromRoute] string itemId)
        {
            await observability.HandlePingAsync(itemId);
            return Ok(new { message = $"Received ping for {itemId}" });
        }

        /// <summary><c>DELETE</c> — removes a health-check configuration.</summary>
        [HttpDelete, Authorize]
        public Task<BaseApiResponse> DeleteHealth([FromQuery] string itemId)
            => observability.DeleteHealthAsync(itemId);
    }
}
