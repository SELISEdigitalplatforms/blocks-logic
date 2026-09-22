namespace Workflow.DomainService.Import
{
    public interface IWorkflowImportTenantSlugResolver
    {
        Task<string?> ResolveAsync(string tenantId);
    }

    public sealed class NullWorkflowImportTenantSlugResolver : IWorkflowImportTenantSlugResolver
    {
        public Task<string?> ResolveAsync(string tenantId) => Task.FromResult<string?>(null);
    }
}
