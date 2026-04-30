using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.RequestModel;
using DomainService.ResponseModel;
using DomainService.Shared;
using DomainService.Shared.RequestModel;
using DomainService.Shared.ResponseModel;
using Iam.DomainService.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.Services
{
    public interface IAuthenticationDomainService
    {
        IEnumerable<string> GetVisitorsIpAddresses(HttpContext context);
        string GetRequestOriginHostName(HttpContext context);
        Task SendToQueueAsync<T>(string queue, T payload) where T : class;
        DeviceInformation? GetDeviceInfo(string userAgent);
        Task<SaveSsoCredentialResponse> SaveSocialLoginCredentialAsync(SaveSsoCredentialRequest credential);
        Task<BaseResponse> DeleteSocialLoginCredentialAsync(string itemId);
        Task<GetSsoCredentialResponse> GetSsoCredentialAsync(string itemId);
        Task<List<SocialLoginCredential>> GetSocialLoginCredentialsAsync();
        Task<BaseResponse> UpdateSsoCredentialStatusAsync(UpdateSsoCredentialStatusRequest request);
        Task<SaveOIDCClientResponse> SaveOIDCClientAsync(SaveOIDCClientRequest request);
        Task<BaseResponse> DeleteOIDCClientAsyncAsync(DeleteOIDCClientRequest request);
        Task<BaseResponse> GenerateUserCodeByClientAsync(GenerateUserCodeRequest request);
        Task<GetOIDCClientResponse> GetOIDCClientAsyncAsync(string tenantId);
        Task<GetOIDCClientsResponse> GetOIDCClientsAsyncAsync();
        Task<BaseResponse> SaveClientCredentialAsync(SaveClientCredentialRequest request);
        Task<BaseResponse> DeleteClientCredentialAsync(DeleteClientCredentialRequest request);
        Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request);
        Task<string> GetOIDCRedirectUriAsync(GetOIDCRedirectUriRequest request);
        Task<IActionResult> GetOIDCToenAsync(GetOIDCTokenRequest request);
    }
}
