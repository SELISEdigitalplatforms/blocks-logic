namespace DomainService.Notification
{
    /// <summary>
    /// Who reads the inbox. <paramref name="ProjectTenantId"/> is set for an impersonated root user: their rows are
    /// in the ROOT DB, and only the rows of that project (or rows without a project) are theirs to see here.
    /// </summary>
    public sealed record InboxViewer(string UserId, IReadOnlyList<string> OrganizationIds, string? ProjectTenantId);
}
