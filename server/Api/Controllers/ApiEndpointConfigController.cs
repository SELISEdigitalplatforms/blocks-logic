using Blocks.Genesis;
using BlocksCloudDomain.Requests;
using BlocksCloudDomain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ApiEndpointConfigController : ControllerBase
    {
        private readonly IApiEndpointConfigService _service;

        public ApiEndpointConfigController(IApiEndpointConfigService service)
        {
            _service = service;
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<IActionResult> GetList([FromBody] GetApiEndpointConfigsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
                return BadRequest(new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "missing_project_key", "ProjectKey is required" } }
                });

            var response = await _service.GetListAsync(request);
            return Ok(response);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<IActionResult> Update([FromBody] UpdateApiEndpointConfigRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
                return BadRequest(new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "missing_project_key", "ProjectKey is required" } }
                });

            var response = await _service.UpdateAsync(request);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
