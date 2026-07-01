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
        public async Task<BaseApiResponse> GetReposList()
        {
            return await deploymentDriverService.GetReposListAsync();
        }

        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetUser()
        {
            return await deploymentDriverService.GetUserAsync();
        }

        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetRepos([FromQuery] string? Search, [FromQuery] int PageNumber = 1, [FromQuery] int PageSize = 30)
        {
            return await deploymentDriverService.SearchRepositoriesAsync(Search, PageNumber, PageSize);
        }

        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetBranches([FromQuery] string repo)
        {
            return await deploymentDriverService.GetBranchesAsync(repo);
        }

        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GithubBranchExists([FromQuery] string repoId)
        {
            return await deploymentDriverService.GithubBranchExistsAsync(repoId);
        }
    }
}
