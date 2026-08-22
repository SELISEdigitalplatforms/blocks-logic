using DomainService.Dtos;

namespace DomainService.People
{
    public interface IPeopleRepository
    {
        Task<(List<GetProjectPeople> peoples, long totalCount, long peoplesTotalCount, bool isOwner)> GetPeoplesAsync(GetPeoplesRequest request);
    }
}
