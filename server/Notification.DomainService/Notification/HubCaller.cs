using System.Security.Claims;

namespace DomainService.Notification
{
    /// <summary>What the hub needs from a connection's token claims.</summary>
    public sealed record HubCaller(string TenantId, string UserId, string? OriginalTenantId, bool Impersonated)
    {
        private const string TenantIdClaim = "tenant_id";
        private const string UserIdClaim = "user_id";
        private const string OriginalTenantIdClaim = "original_tenant_id";
        private const string ImpersonatedClaim = "impersonated";

        // Compared the same way Genesis reads the claim when it builds a context from an HTTP request, so an
        // impersonated connection and an impersonated request agree on which database is used.
        public static HubCaller From(ClaimsPrincipal? user) => new(
            user?.FindFirst(TenantIdClaim)?.Value ?? string.Empty,
            user?.FindFirst(UserIdClaim)?.Value ?? string.Empty,
            user?.FindFirst(OriginalTenantIdClaim)?.Value,
            user?.FindFirst(ImpersonatedClaim)?.Value == "true");

        /// <summary>
        /// The tenants whose sends reach this connection: the token tenant, and for an impersonated root user also
        /// the original (root) tenant, because Blocks services send to root users with the root key.
        /// </summary>
        public IReadOnlyList<string> GroupTenantIds()
        {
            var tenantIds = new List<string>();
            if (!string.IsNullOrWhiteSpace(TenantId))
                tenantIds.Add(TenantId);

            if (Impersonated && !string.IsNullOrWhiteSpace(OriginalTenantId) && !tenantIds.Contains(OriginalTenantId))
                tenantIds.Add(OriginalTenantId);

            return tenantIds;
        }

        /// <summary>
        /// The tenant the user's organizations belong to: the root tenant for an impersonated root user (the user and
        /// its organizations live there), the token tenant otherwise.
        /// </summary>
        public string? OrganizationTenantId()
        {
            var tenantId = Impersonated && !string.IsNullOrWhiteSpace(OriginalTenantId) ? OriginalTenantId : TenantId;
            return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
        }
    }
}
