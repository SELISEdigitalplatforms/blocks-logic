using System.Security.Claims;
using Blocks.Genesis;

namespace Common.InternalService.Access
{
    public enum EndpointAccessStatus
    {
        /// <summary>The caller may invoke the endpoint. <see cref="EndpointAccessDecision.Context"/> is null for a public policy.</summary>
        Allowed = 0,

        /// <summary>A Blocks token was required and none was presented, or it did not validate. Maps to 401.</summary>
        Unauthenticated = 1,

        /// <summary>The token validated but the caller fails the configured role / permission rules. Maps to 403.</summary>
        Forbidden = 2,
    }

    /// <summary>The outcome of <see cref="IEndpointAccessAuthorizer.AuthorizeAsync"/>.</summary>
    public sealed record EndpointAccessDecision(
        EndpointAccessStatus Status,
        BlocksContext? Context,
        ClaimsPrincipal? Principal,
        string? RawToken,
        string? Reason)
    {
        public bool IsAllowed => Status == EndpointAccessStatus.Allowed;

        public static EndpointAccessDecision Public() =>
            new(EndpointAccessStatus.Allowed, null, null, null, null);

        public static EndpointAccessDecision Allowed(BlocksContext context, ClaimsPrincipal principal, string? rawToken) =>
            new(EndpointAccessStatus.Allowed, context, principal, rawToken, null);

        public static EndpointAccessDecision Unauthenticated(string reason) =>
            new(EndpointAccessStatus.Unauthenticated, null, null, null, reason);

        public static EndpointAccessDecision Forbidden(string reason) =>
            new(EndpointAccessStatus.Forbidden, null, null, null, reason);
    }
}
