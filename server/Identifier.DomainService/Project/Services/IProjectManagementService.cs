using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.Projects
{
    public interface IProjectManagementService
    {
        Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request);
        Task<GetProjectResponse> GetAsync();
    }
}
