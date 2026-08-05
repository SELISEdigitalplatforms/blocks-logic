using Blocks.Genesis;
using DomainService.Entities;


namespace DomainService.Projects
{
    public class GetProjectResponse : BaseQueryResponse<GetProjectResponseData>
    {

    }
    public class GetProjectResponseData : Project
    {
        public string TenantSlug { get; set; }
    }
}
