using Scheduler.DomainService.Dtos;
using Scheduler.DomainService.Dtos.Responses;
using Scheduler.DomainService.Entities;

namespace Scheduler.DomainService.Repositories
{
    public interface IScheduleRepository
    {
        Task<Schedule?> GetByIdAsync(string scheduleId, string tenantId = "");
        Task CreateAsync(Schedule schedule);
        Task UpdateAsync(Schedule schedule);
        Task<bool> DeleteAsync(string itemId, string tenantId = "");
        Task<List<SchedularDto>> GetSchedulesFromAllTenantsAsync();
        Task<(List<Schedule>? Items, long TotalCount)> GetAllAsync(string searchKey, int pageNumber, int pageSize);
    }
}
