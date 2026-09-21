using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Access
{
    /// <summary>
    /// The "Who can call it" setting of a tenant-published endpoint, shared by every module that exposes
    /// one (proxy gateway routes, workflow webhooks, ...). It is data, not an attribute: the same
    /// controller action serves many endpoints whose policies differ, so the decision has to be made
    /// per stored policy at request time by <see cref="IEndpointAccessAuthorizer"/>.
    /// <list type="bullet">
    /// <item><see cref="EndpointAccessKind.Public"/> — anyone with the URL. Rules are ignored.</item>
    /// <item><see cref="EndpointAccessKind.BlocksToken"/> — a valid Blocks token for the tenant is
    /// required. With no rules configured any signed-in caller may invoke it; otherwise the configured
    /// <see cref="Roles"/> / <see cref="Permissions"/> rules apply, combined by <see cref="Combine"/>.</item>
    /// </list>
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class EndpointAccessPolicy
    {
        [BsonRepresentation(BsonType.String)]
        public EndpointAccessKind Kind { get; set; } = EndpointAccessKind.BlocksToken;

        /// <summary>Optional organization the caller must belong to. Empty ⇒ no restriction.</summary>
        public string OrganizationId { get; set; } = string.Empty;

        public EndpointAccessRule Roles { get; set; } = new();

        public EndpointAccessRule Permissions { get; set; } = new();

        [BsonRepresentation(BsonType.String)]
        public EndpointAccessCombine Combine { get; set; } = EndpointAccessCombine.Or;

        public bool IsPublic => Kind == EndpointAccessKind.Public;

        /// <summary><c>true</c> when at least one of the role / permission rules constrains callers.</summary>
        public bool HasRestrictions => Roles.IsConfigured || Permissions.IsConfigured;

        /// <summary>The safe default: a Blocks token is required, any signed-in caller may invoke.</summary>
        public static EndpointAccessPolicy RequireToken() => new();

        public static EndpointAccessPolicy AllowPublic() => new() { Kind = EndpointAccessKind.Public };

        public EndpointAccessPolicy Clone() => new()
        {
            Kind = Kind,
            OrganizationId = OrganizationId,
            Roles = Roles.Clone(),
            Permissions = Permissions.Clone(),
            Combine = Combine,
        };
    }
}
