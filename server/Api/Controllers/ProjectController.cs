using DomainService.Dtos;
using DomainService.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace Api.Controllers
{
    /// <summary>Project listing and the caller's current project, routed as <c>/api/Project/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;

        /// <summary>Takes the project management service.</summary>
        public ProjectController(IProjectManagementService projectManagementService)
        {
            _projectManagementService = projectManagementService;
        }

        /// <summary><c>GET</c> — the caller's projects, grouped for the project switcher.</summary>
        [HttpGet]
        [Authorize]
        public async Task<List<GroupedProjectsDto>> Gets([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        /// <summary><c>GET</c> — the project the current request context resolves to.</summary>
        [HttpGet]
        [Authorize]
        public async Task<GetProjectResponse> Get()
        {
            return await _projectManagementService.GetAsync();
        }
    }
}