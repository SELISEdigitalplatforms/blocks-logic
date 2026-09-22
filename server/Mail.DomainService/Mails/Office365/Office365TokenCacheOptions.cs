namespace Mail.DomainService.Mails.Office365
{
    /// <summary>A clock the tests can move.</summary>
    public interface ISystemClock
    {
        DateTimeOffset UtcNow { get; }
    }

    /// <inheritdoc />
    public sealed class SystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    /// <summary>Process-level limits on the Office 365 token cache.</summary>
    public sealed class Office365TokenCacheOptions
    {
        /// <summary>
        /// How long before expiry a token stops being reused.
        /// </summary>
        /// <remarks>
        /// A token that expires mid-session is refused by SMTP with an authentication error that
        /// looks exactly like a misconfigured application, so the window is generous enough that a
        /// send which passes the check has time to finish.
        /// </remarks>
        public TimeSpan RefreshWindow { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The most completed entries kept. Bounded because the key includes the tenant, so an
        /// unbounded cache grows with the number of tenants rather than with load.
        /// </summary>
        public int MaxEntries { get; set; } = 1000;

        /// <summary>
        /// How long one acquisition may run before it is abandoned.
        /// </summary>
        /// <remarks>
        /// Shared by every caller joined to it, so without a bound one stuck request to Entra
        /// would hold every send for that configuration open indefinitely.
        /// </remarks>
        public TimeSpan AcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// What a cached token is keyed by.
    /// </summary>
    /// <remarks>
    /// Every field participates, and all of them compare ordinally. The Blocks tenant is in the
    /// key because a token is bought with that tenant's secret; the reference is in it because a
    /// configuration repointed at a different secret must not keep using the old token.
    /// </remarks>
    public readonly record struct Office365TokenCacheKey(
        string BlocksTenantId,
        string EntraTenantId,
        string ClientId,
        string ClientSecretReference)
    {
        public static Office365TokenCacheKey From(Office365TokenRequest request) =>
            new(request.BlocksTenantId, request.EntraTenantId, request.ClientId, request.ClientSecretReference);
    }
}
