using Functions.DomainService.Entities;

namespace Functions.DomainService.Repositories
{
    public interface IFunctionAuditRepository
    {
        Task CreateAsync(string tenantId, FunctionAuditEventEntity auditEvent, CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<FunctionAuditEventEntity> Items, long TotalCount)> GetByFunctionAsync(
            string tenantId, string functionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    }
}
