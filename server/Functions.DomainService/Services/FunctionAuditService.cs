using System.Text.Json;
using Functions.DomainService.Entities;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Services
{
    public interface IFunctionAuditService
    {
        Task RecordAsync(
            string tenantId, string functionId, string action,
            string? actorId, string? actorEmail, object? detail = null,
            CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<FunctionAuditEventEntity> Items, long TotalCount)> GetAsync(
            string tenantId, string functionId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Records what happened to a function and by whom. Never a reason to fail the caller's
    /// request: an audit write that could not be persisted is logged and swallowed rather than
    /// surfaced, because losing an audit entry is regrettable but losing the deploy that
    /// prompted it would be worse.
    /// </summary>
    public class FunctionAuditService : IFunctionAuditService
    {
        private readonly IFunctionAuditRepository _repository;
        private readonly ILogger<FunctionAuditService> _logger;

        public FunctionAuditService(IFunctionAuditRepository repository, ILogger<FunctionAuditService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task RecordAsync(
            string tenantId, string functionId, string action,
            string? actorId, string? actorEmail, object? detail = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var entry = new FunctionAuditEventEntity
                {
                    ItemId = Guid.NewGuid().ToString(),
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    FunctionId = functionId,
                    Action = action,
                    ActorId = actorId,
                    ActorEmail = actorEmail,
                    // Never a secret value: callers pass structured context (e.g. version
                    // number, rollback target), not anything resolved from ISecretService.
                    Detail = detail is null ? null : JsonSerializer.Serialize(detail),
                };

                await _repository.CreateAsync(tenantId, entry, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record audit event {Action} for function {FunctionId}", action, functionId);
            }
        }

        public Task<(IReadOnlyList<FunctionAuditEventEntity> Items, long TotalCount)> GetAsync(
            string tenantId, string functionId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
            => _repository.GetByFunctionAsync(tenantId, functionId, pageNumber, pageSize, cancellationToken);
    }
}
