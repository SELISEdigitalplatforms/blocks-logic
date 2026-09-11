
using Blocks.Genesis;

namespace DomainService.Shared.RequestModel
{
    public class SaveClientCredentialRequest
    {
        public string Name { get; set; }
        public List<string> Roles { get; set; }
    }

    public class DeleteClientCredentialRequest
    {
        public string ItemId { get; set; }
    }
}
