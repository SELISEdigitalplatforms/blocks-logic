using Microsoft.AspNetCore.Http;

namespace DomainService.Geolocation
{
    public interface IGeolocationService
    {
        Task<LocateIpResponse> LocateIpAsync(LocateIpRequest request);
        Task<LocateIpResponse> LocateAsync(LocateRequest request, IEnumerable<string> ipAddresses);
        IEnumerable<string> GetVisitorsIpAddresses(HttpContext httpContext);
    }
}