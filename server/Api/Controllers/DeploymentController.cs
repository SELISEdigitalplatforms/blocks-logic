using System.Net;
using Blocks.Genesis;
using DeploymentDriver;
using Devops.DomainService.Deployment.Models.Request;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Source-control integration behind the deployment feature, routed as
    /// <c>/api/Deployment/{action}</c>. Every action requires a bearer token and delegates straight to
    /// <see cref="IDeploymentDriverService"/>, which talks to the connected provider.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class DeploymentController(IDeploymentDriverService deploymentDriverService) : ControllerBase
    {
        /// <summary><c>GET</c> — whether this tenant has a working provider authorization.</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> IsAuthorized()
        {
            return await deploymentDriverService.IsAuthorizeAsync();
        }

        /// <summary><c>GET</c> — exchanges an OAuth callback <paramref name="code"/> for an access token and stores it.</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> AccessToken([FromQuery] string code)
        {
            return await deploymentDriverService.GetAccessTokenAsync(code);
        }

        /// <summary><c>POST</c> — revokes the stored authorization, leaving the connection record in place.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseApiResponse> RemoveAuthorization()
        {
            return await deploymentDriverService.RemoveAuthorizationAsync();
        }

        /// <summary><c>DELETE</c> — removes the stored authorization outright.</summary>
        [HttpDelete]
        [Authorize]
        public async Task<BaseApiResponse> DeleteAuthorization()
        {
            return await deploymentDriverService.DeleteAuthorizationAsync();
        }

        /// <summary><c>GET</c> — every repository the authorization can see.</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetReposList()
        {
            return await deploymentDriverService.GetReposListAsync();
        }

        /// <summary><c>GET</c> — the provider account the authorization belongs to.</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetUser()
        {
            return await deploymentDriverService.GetUserAsync();
        }

        /// <summary><c>GET</c> — repository search, paged (1-based <paramref name="PageNumber"/>, default 30 per page).</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetRepos([FromQuery] string? Search, [FromQuery] int PageNumber = 1, [FromQuery] int PageSize = 30)
        {
            return await deploymentDriverService.SearchRepositoriesAsync(Search, PageNumber, PageSize);
        }

        /// <summary><c>GET</c> — the branches of one repository.</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GetBranches([FromQuery] string repo)
        {
            return await deploymentDriverService.GetBranchesAsync(repo);
        }

        /// <summary><c>GET</c> — whether the expected branch exists on the given GitHub repository.</summary>
        [HttpGet]
        [Authorize]
        public async Task<BaseApiResponse> GithubBranchExists([FromQuery] string repoId)
        {
            return await deploymentDriverService.GithubBranchExistsAsync(repoId);
        }

        /// <summary><c>POST</c> — points a repository at a different deployment domain.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseApiResponse> UpdateRepoDomain([FromBody] RepoDomainUpdateRequest request)
        {
            return await deploymentDriverService.UpdateRepoDomainAsync(request);
        }
    }
}
