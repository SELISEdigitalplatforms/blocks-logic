using DomainService.Dtos;
using DomainService.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;

        public ProjectController(IProjectManagementService projectManagementService)
        {
            _projectManagementService = projectManagementService;
        }

        [HttpGet]
        [Authorize]
        public async Task<List<GroupedProjectsDto>> Gets([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<GetProjectResponse> Get()
        {
            return await _projectManagementService.GetAsync();
        }
    }
}