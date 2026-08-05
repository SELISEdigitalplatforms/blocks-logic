using DomainService.Dtos;

namespace DomainService.People
{
    public interface IPeopleService
    {
        Task<GetPeoplesResponse> GetPeoplesAsync(GetPeoplesRequest request);
    }
}