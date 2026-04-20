using Cloud.DomainService.Models;
using Cloud.DomainService.Requests;

namespace Cloud.DomainService.Repositories
{
    public interface IApiEndpointConfigRepository
    {
        Task<(List<ApiEndpointConfig>, long)> GetListAsync(GetApiEndpointConfigsRequest request);
        Task<bool> UpdateAsync(string projectKey, ApiEndpointConfig config);
        Task<long> BulkUpdateAsync(string projectKey, List<string> itemIds, bool isCaptchaRequired, bool isMfaRequired, string updatedBy);
    }
}
