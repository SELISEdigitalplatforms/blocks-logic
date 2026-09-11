using Blocks.Genesis;

namespace DomainService.RequestModel
{
    public class UpdateSsoCredentialStatusRequest
    {
        public string ItemId { get; set; }
        public bool IsEnabled { get; set; }
    }
}
