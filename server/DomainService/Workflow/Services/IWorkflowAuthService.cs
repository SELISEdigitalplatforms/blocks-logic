using Microsoft.AspNetCore.Http;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowAuthService
    {
        public Task<bool> IsAuthenticated(HttpRequest request, string tenantId);

        /// <summary>
        /// Authenticates the caller first (same as <see cref="IsAuthenticated"/>), then enforces the
        /// webhook's configured organization + roles + permissions rules. Returns <c>false</c> if either
        /// the caller is not authenticated or any rule fails.
        /// </summary>
        public Task<bool> IsAuthorized(HttpRequest request, string tenantId, WorkflowAuthService.AuthorizationConfig config);
    }
}