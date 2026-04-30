using System;
using System.Collections.Generic;
using System.Text;

namespace DomainService.OAuth.RequestModel
{
    public class GetOIDCTokenRequest
    {
        public string Code { get; set; }
        public string? State { get; set; }
        public string? Nonce { get; set; }
    }
}
