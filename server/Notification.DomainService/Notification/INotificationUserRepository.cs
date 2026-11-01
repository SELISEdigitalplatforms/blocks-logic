namespace DomainService.Notification
{
    /// <summary>Reads the iam <c>Users</c> fields notifications need. Data access only.</summary>
    public interface INotificationUserRepository
    {
        /// <summary>
        /// The organizations of <paramref name="userId"/> in the caller's DB (the ROOT DB for an impersonated caller,
        /// as every other notification read). Empty when the user is not found.
        /// </summary>
        Task<IReadOnlyList<string>> GetOrganizationIdsAsync(string userId);

        /// <summary>The language of each user found, by user id, in the tenant DB or, with <paramref name="rootDb"/>, the ROOT DB.</summary>
        Task<IReadOnlyDictionary<string, string>> GetLanguagesAsync(string tenantId, IReadOnlyCollection<string> userIds, bool rootDb, CancellationToken cancellationToken);
    }
}
