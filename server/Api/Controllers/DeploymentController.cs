using System.Net;
using Blocks.Genesis;
using DeploymentDriver;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class DeploymentController(IDeploymentDriverService deploymentDriverService) : ControllerBase
    {
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> IsAuthorized()
        {
            return await deploymentDriverService.IsAuthorizeAsync();
        }

        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> AccessToken([FromQuery] string code)
        {
            return await deploymentDriverService.GetAccessTokenAsync(code);
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseApiResponse> RemoveAuthorization()
        {
            return await deploymentDriverService.RemoveAuthorizationAsync();
        }

        [HttpDelete]
        [Authorize]
        public async Task<BaseApiResponse> DeleteAuthorization()
        {
            return await deploymentDriverService.DeleteAuthorizationAsync();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetReposList([FromQuery] string ProjectKey)
        {
            var repoList = await deploymentDriverService.GetReposListAsync(ProjectKey);
            if (repoList != null)
            {
                return Ok(new BaseApiResponse()
                {
                    Data = repoList,
                    IsSuccess = true,
                    StatusCode = HttpStatusCode.OK
                });
            }

            return BadRequest(new BaseApiResponse()
            {
                IsSuccess = false,
                Message = "Failed to get repos.",
            });
        }
    }
}
