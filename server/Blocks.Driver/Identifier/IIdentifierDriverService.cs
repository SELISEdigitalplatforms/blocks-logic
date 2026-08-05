using DomainService.Dtos;
using DomainService.ManagedService;
using DomainService.People;
using DomainService.Projects;

namespace Blocks.Driver.Identifier;

/// <summary>
/// Service for retrieving people, project and managed-service information owned by the Identifier domain.
/// </summary>
public interface IIdentifierDriverService
{
    /// <summary>
    /// Gets the people shared across the projects in the given project group.
    /// </summary>
    Task<GetPeoplesResponse> GetPeoplesAsync(GetPeoplesRequest request);
    /// <summary>
    /// Gets all projects grouped by tenant group, ordered by last modified date.
    /// </summary>
    Task<List<GroupedProjectsDto>> GetProjectsAsync(GetProjectsRequest request);
    /// <summary>
    /// Gets the project for the tenant in the current Blocks context.
    /// </summary>
    Task<GetProjectResponse> GetCurrentProjectAsync();
    /// <summary>
    /// Gets all managed services matching the given filter.
    /// </summary>
    Task<GetAllServiceResponse> GetAllServicesAsync(GetAllServiceRequest request);
}
