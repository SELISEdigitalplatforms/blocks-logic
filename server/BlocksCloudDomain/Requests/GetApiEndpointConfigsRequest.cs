using Blocks.Genesis;

namespace BlocksCloudDomain.Requests
{
    public class GetApiEndpointConfigsRequest : BaseGetsRequest<ApiEndpointConfigFilter>, IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
    }

    public class ApiEndpointConfigFilter
    {
        public string? Service { get; set; }
        public string? Method { get; set; }
        public string? Endpoint { get; set; }
    }
}
