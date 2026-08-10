using Blocks.Genesis;
using Iam.DomainService.Users;

namespace Blocks.Driver.Iam;

public class IamDriverService : IIamDriverService
{
    private readonly IUserManagementMutationService _userManagementMutationService;

    public IamDriverService(IUserManagementMutationService userManagementMutationService)
    {
        _userManagementMutationService = userManagementMutationService;
    }

    public Task<BaseMutationResponse> CreateUser(CreateUserRequest createUserRequest)
        => _userManagementMutationService.CreateUserAsync(createUserRequest);

    public Task<BaseMutationResponse> CreateUserViaSso(CreateUserViaSsoRequest command)
        => _userManagementMutationService.CreateUserViaSsoAsync(command);
}
