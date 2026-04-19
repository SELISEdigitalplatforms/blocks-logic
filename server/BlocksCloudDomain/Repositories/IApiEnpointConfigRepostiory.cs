using BlocksCloudDomain.Models;
using BlocksCloudDomain.Requests;

namespace BlocksCloudDomain.Repositories
{
    public interface IApiEndpointConfigRepository
    {
        Task<(List<ApiEndpointConfig>, long)> GetListAsync(GetApiEndpointConfigsRequest request);
        Task<bool> UpdateAsync(string projectKey, ApiEndpointConfig config);
    }
}
