using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetOrganizationResponse: BaseResponse
    {
        public Organization Organization { get; set; }
    }
}
