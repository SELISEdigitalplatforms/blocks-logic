using Blocks.Genesis;

namespace Cloud.DomainService.Requests
{
    public class UpdateApiEndpointConfigRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsCaptchaRequired { get; set; }
        public string? CaptchaProvider { get; set; }
        public bool IsMfaRequired { get; set; }
        public string? MfaType { get; set; }
    }
}
