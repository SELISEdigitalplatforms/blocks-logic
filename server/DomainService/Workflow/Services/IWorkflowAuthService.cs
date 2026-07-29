using Microsoft.AspNetCore.Http;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowAuthService
    {
        public Task<bool> IsAuthenticated(HttpRequest request, string tenantId);
    }
}