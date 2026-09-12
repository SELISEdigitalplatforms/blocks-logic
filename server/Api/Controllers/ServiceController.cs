using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Blocks.Genesis;

namespace Api.Controllers
{
    /// <summary>Service registry lookups, routed as <c>/api/Service/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ServiceController : ControllerBase
    {
        private readonly IServiceManagement _serviceManagement;

        /// <summary>Takes the service-management service.</summary>
        public ServiceController(IServiceManagement serviceManagement)
        {
            _serviceManagement = serviceManagement;
        }

        /// <summary><c>POST</c> — the registered services, filtered and paged by the request body.</summary>
        [Authorize]
        [HttpPost]
        public async Task<GetAllServiceResponse> GetAll([FromBody] GetAllServiceRequest request)
        {
            return await _serviceManagement.GetAllServicesAsync(request);
        }
    }
}