using Microsoft.AspNetCore.Http;
using Blocks.Genesis;

namespace DomainService.Workflow.Services
{
    public interface IWorkflowAuthService
    {
        public Task<bool> IsAuthenticated(HttpRequest request, string tenantId);

        /// <summary>
        /// Authenticates the caller first (same as <see cref="IsAuthenticated"/>), then enforces the
        /// webhook's configured organization + roles + permissions rules. Returns <c>false</c> if either
        /// the caller is not authenticated or any rule fails.
        /// On success, also returns a <see cref="BlocksContext"/> built from the validated JWT
        /// and assigns the principal to <c>request.HttpContext.User</c>.
        /// </summary>
        public Task<(bool isAuthorized, BlocksContext? context)> IsAuthorized(HttpRequest request, string tenantId, WorkflowAuthService.AuthorizationConfig config);

        /// <summary>
        /// Best-effort: returns a Blocks-delegated bearer token for the current ambient context, or
        /// <c>null</c> when no delegation grant is available (e.g. most trigger-originated workflow
        /// runs today). Callers must treat <c>null</c> as "omit the Authorization header", not as an error.
        /// </summary>
        public Task<string?> CreateBlocksAuthorizationTokenAsync(CancellationToken ct = default);
    }
}