using System.Security.Claims;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;

namespace Common.InternalService.Access
{
    /// <summary>
    /// Runtime enforcement of an <see cref="EndpointAccessPolicy"/> on an anonymous (at the framework
    /// level) endpoint. The Genesis bearer handler skips token parsing entirely on <c>[AllowAnonymous]</c>
    /// actions, so any endpoint whose policy is stored per row has to validate the token itself; this is
    /// the one place that does so, for every module.
    /// </summary>
    public interface IEndpointAccessAuthorizer
    {
        /// <summary>
        /// Resolves the tenant a data-plane call belongs to: the <c>x-blocks-key</c> header (or the query /
        /// form equivalents Genesis accepts), falling back to the <c>tenant_id</c> claim of a presented
        /// bearer token. The claim is read unverified and is only used to pick the tenant whose certificate
        /// then validates that same token. <c>null</c> when neither is present.
        /// </summary>
        Task<string?> ResolveTenantIdAsync(HttpRequest request);

        /// <summary>
        /// Enforces <paramref name="policy"/> for <paramref name="request"/>. A public policy is allowed
        /// without looking at credentials. Otherwise the bearer token is validated against the tenant's
        /// public certificate, the rules are evaluated, and on success a <see cref="BlocksContext"/> is
        /// built and the principal is assigned to <c>HttpContext.User</c> so ambient
        /// <see cref="BlocksContext.GetContext"/> callers see the authenticated user.
        /// </summary>
        Task<EndpointAccessDecision> AuthorizeAsync(
            HttpRequest request, string tenantId, EndpointAccessPolicy policy, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the request's bearer token against <paramref name="tenantId"/>'s public certificate.
        /// Returns <c>(null, null)</c> on any failure (missing token, unknown tenant, bad signature, expired).
        /// Exposed so a module that only needs "is this a valid Blocks caller" shares the same validation.
        /// </summary>
        Task<(ClaimsPrincipal? Principal, string? RawToken)> ValidateTokenAsync(HttpRequest request, string tenantId);

        /// <summary>Builds the <see cref="BlocksContext"/> for a validated caller of <paramref name="tenantId"/>; <c>null</c> for an unknown tenant.</summary>
        BlocksContext? BuildContext(HttpRequest request, string tenantId, ClaimsPrincipal principal, string? rawToken);
    }
}
