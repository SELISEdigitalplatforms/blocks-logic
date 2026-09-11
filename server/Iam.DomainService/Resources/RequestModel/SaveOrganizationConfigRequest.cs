
using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class SaveOrganizationConfigRequest
    {
        public string? ItemId { get; set; }
        public bool AllowCreationFromCloud { get; set; }
        public bool AllowCreationFromConstruct { get; set; }
        public List<string> Roles { get; set; } = [];
        public bool IsMultiOrgEnabled { get; set; }
    }

    public class GetOrganizationConfigRequest
    {
    }
}
