using DomainService.Dtos;
using DomainService.ManagedService;
using DomainService.People;
using DomainService.Projects;

namespace Blocks.Driver.Identifier;

public class IdentifierDriverService : IIdentifierDriverService
{
    private readonly IPeopleService _peopleService;
    private readonly IProjectManagementService _projectManagementService;
    private readonly IServiceManagement _serviceManagement;

    public IdentifierDriverService(
        IPeopleService peopleService,
        IProjectManagementService projectManagementService,
        IServiceManagement serviceManagement)
    {
        _peopleService = peopleService;
        _projectManagementService = projectManagementService;
        _serviceManagement = serviceManagement;
    }

    public Task<GetPeoplesResponse> GetPeoplesAsync(GetPeoplesRequest request)
        => _peopleService.GetPeoplesAsync(request);

    public Task<List<GroupedProjectsDto>> GetProjectsAsync(GetProjectsRequest request)
        => _projectManagementService.GetAllAsync(request);

    public Task<GetProjectResponse> GetCurrentProjectAsync()
        => _projectManagementService.GetAsync();

    public Task<GetAllServiceResponse> GetAllServicesAsync(GetAllServiceRequest request)
        => _serviceManagement.GetAllServicesAsync(request);
}
