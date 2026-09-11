using Blocks.Genesis;
using DomainService.Entities;

namespace DomainService.RequestModel
{
    public class GetOIDCClientRequest
    {
        public string ClientId { get; set; }    
    }

    public class GetOIDCClientsRequest
    {
    }

    public class DeleteOIDCClientRequest
    {
        public string ItemId { get; set; }
    }

    public class GetOIDCClientsResponse : BaseResponse
    {
        public List<OIDCClientCredential> oIDCClientCredentials { get; set; }
    }

    public class GetOIDCClientResponse : BaseResponse
    {
        public OIDCClientCredential oIDCClientCredential { get; set; }
    }
}
