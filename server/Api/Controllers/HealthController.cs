using DomainService.Health.Models;
using DomainService.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObservabilityDriver;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class HealthController(IObservabilityDriverService observability) : ControllerBase
    {
        [HttpPost, Authorize]
        public Task<BaseApiResponse> SaveHealth([FromBody] SaveHealthConfigurationRequest request)
            => observability.SaveHealthAsync(request);

        [HttpPost, Authorize]
        public Task<BaseApiResponse> UpdateHealth([FromBody] UpdateHealthConfigurationRequest request)
            => observability.UpdateHealthAsync(request);

        [HttpGet("{itemId}")]
        public async Task<IActionResult> Ping([FromRoute] string itemId)
        {
            await observability.HandlePingAsync(itemId);
            return Ok(new { message = $"Received ping for {itemId}" });
        }

        [HttpDelete, Authorize]
        public Task<BaseApiResponse> DeleteHealth([FromQuery] string itemId)
            => observability.DeleteHealthAsync(itemId);
    }
}
