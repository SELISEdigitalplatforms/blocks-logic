using Blocks.Genesis;
using BlocksCloudDomain.Models;
using BlocksCloudDomain.Repositories;
using BlocksCloudDomain.Requests;
using BlocksCloudDomain.Responses;

namespace BlocksCloudDomain.Services
{
    public class ApiEndpointConfigService : IApiEndpointConfigService
    {
        private readonly IApiEndpointConfigRepository _repository;

        public ApiEndpointConfigService(IApiEndpointConfigRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetApiEndpointConfigsResponse> GetListAsync(GetApiEndpointConfigsRequest request)
        {
            var (data, count) = await _repository.GetListAsync(request);

            return new GetApiEndpointConfigsResponse
            {
                Data = data.AsQueryable(),
                TotalCount = count
            };
        }

        public async Task<BaseResponse> UpdateAsync(UpdateApiEndpointConfigRequest request)
        {
            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

            var config = new ApiEndpointConfig
            {
                ItemId = request.ItemId,
                Service = request.Service,
                Method = request.Method,
                Endpoint = request.Endpoint,
                IsEnabled = request.IsEnabled,
                Description = request.Description,
                LastUpdatedBy = userId,
                LastUpdatedDate = DateTime.UtcNow
            };

            var success = await _repository.UpdateAsync(request.ProjectKey, config);

            return new BaseResponse
            {
                IsSuccess = success,
                Errors = success
                    ? []
                    : new Dictionary<string, string> { { "update_failed", "No matching record found to update" } }
            };
        }
    }
}
