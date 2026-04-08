using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetOrganizationsResponse : BaseResponse
    {
        public List<Organization> Organizations { get; set; }
        public long TotalCount { get; set; }
    }
}
