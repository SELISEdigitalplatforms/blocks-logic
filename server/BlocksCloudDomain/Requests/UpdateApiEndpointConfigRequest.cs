using Blocks.Genesis;

namespace BlocksCloudDomain.Requests
{
    public class UpdateApiEndpointConfigRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? Description { get; set; }
    }
}
