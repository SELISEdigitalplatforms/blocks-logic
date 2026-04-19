using Blocks.Genesis;
using BlocksCloudDomain.Requests;
using BlocksCloudDomain.Responses;

namespace BlocksCloudDomain.Services
{
    public interface IApiEndpointConfigService
    {
        Task<GetApiEndpointConfigsResponse> GetListAsync(GetApiEndpointConfigsRequest request);
        Task<BaseResponse> UpdateAsync(UpdateApiEndpointConfigRequest request);
    }
}
