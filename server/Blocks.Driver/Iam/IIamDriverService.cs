using Blocks.Genesis;
using Iam.DomainService.Users;

namespace Blocks.Driver.Iam;

/// <summary>
/// Service for handling IAM (Identity and Access Management) user operations.
/// </summary>
public interface IIamDriverService
{
    Task<BaseMutationResponse> CreateUser(CreateUserRequest createUserRequest);
    Task<BaseMutationResponse> CreateUserViaSso(CreateUserViaSsoRequest command);
}
