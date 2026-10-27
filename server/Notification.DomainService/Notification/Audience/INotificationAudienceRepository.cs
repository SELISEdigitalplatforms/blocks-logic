namespace DomainService.Notification
{
    /// <summary>The fields of an IAM user that audience rules read. Roles and Permissions map an organization id to its list.</summary>
    public sealed record AudienceUser(
        string ItemId,
        IReadOnlyList<string> OrganizationIds,
        IReadOnlyDictionary<string, List<string>> Roles,
        IReadOnlyDictionary<string, List<string>> Permissions);

    /// <summary>
    /// Optional narrowing done by the database. Each set list must match (AND); an empty list does not narrow.
    /// </summary>
    public sealed record AudienceUserQuery(IReadOnlyCollection<string> UserIds, IReadOnlyCollection<string> OrganizationIds)
    {
        public static AudienceUserQuery Everyone { get; } = new([], []);
    }

    /// <summary>A permission row granted to roles inside one organization.</summary>
    public sealed record PermissionRoleGrant(string OrganizationId, IReadOnlyList<string> Roles);

    /// <summary>Reads IAM users and permissions for audience rules. Only ids and grant lists, never profile data.</summary>
    public interface INotificationAudienceRepository
    {
        /// <summary>Active users (Active and Status missing or Active) of the request tenant, streamed in batches.</summary>
        IAsyncEnumerable<AudienceUser> GetActiveUsersAsync(AudienceUserQuery query, CancellationToken cancellationToken = default);

        /// <summary>Not archived permission rows whose Resource is one of <paramref name="resources"/> and that list roles.</summary>
        Task<List<PermissionRoleGrant>> GetRoleGrantsAsync(IReadOnlyCollection<string> resources, CancellationToken cancellationToken = default);
    }
}
