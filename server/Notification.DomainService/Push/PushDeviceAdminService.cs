namespace DomainService.Notification.Push
{
    /// <summary>Query of <c>GET Notifier/devices/all</c>.</summary>
    public sealed class PushDeviceListRequest
    {
        public string? Platform { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = PushDeviceAdminService.DefaultPageSize;
    }

    /// <summary>One device in the admin list. Never the token.</summary>
    public sealed record PushDeviceRow(string Id, string UserId, string UserName, string Platform, string? Name, DateTime LastSeenAt);

    public sealed record PushDeviceCounts(int All, int Android, int Ios, int Web);

    /// <summary>Answer of <c>GET Notifier/devices/all</c>. Counts follow the search, not the platform filter.</summary>
    public sealed record PushDeviceList(IReadOnlyList<PushDeviceRow> Items, long TotalCount, PushDeviceCounts Counts);

    /// <summary>Every device of the context tenant, for admins (screen 3).</summary>
    public interface IPushDeviceAdminService
    {
        Task<PushDeviceList> ListAsync(PushDeviceListRequest request, CancellationToken cancellationToken);

        /// <summary>False when the tenant has no device with this id.</summary>
        Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushDeviceAdminService"/>
    /// <remarks>Tenant DB only: ROOT DB rows (console users' devices) are never listed or removed here.</remarks>
    public sealed class PushDeviceAdminService : IPushDeviceAdminService
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;
        public const int MaxSearchLength = 100;
        public const int MaxSearchUsers = 1000;
        private const int MaxPage = 100_000;
        private const string NoPlatform = "-";

        private readonly IPushDeviceService _context;
        private readonly IPushDeviceStore _devices;
        private readonly IPushUserRepository _users;
        private readonly TimeProvider _timeProvider;

        public PushDeviceAdminService(IPushDeviceService context, IPushDeviceStore devices, IPushUserRepository users, TimeProvider timeProvider)
        {
            _context = context;
            _devices = devices;
            _users = users;
            _timeProvider = timeProvider;
        }

        public Task<PushDeviceList> ListAsync(PushDeviceListRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ListCoreAsync(request, cancellationToken);
        }

        private async Task<PushDeviceList> ListCoreAsync(PushDeviceListRequest request, CancellationToken cancellationToken)
        {
            var db = _context.TenantDb();
            var seenSince = NotificationDevice.SeenSince(_timeProvider.GetUtcNow().UtcDateTime);
            var userIds = await SearchAsync(db.TenantId, request.Search, cancellationToken);
            if (userIds is { Count: 0 })
                return new PushDeviceList([], 0, new PushDeviceCounts(0, 0, 0, 0));

            var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
            var page = Math.Clamp(request.Page, 1, MaxPage);
            var counts = await _devices.CountByPlatformAsync(db, seenSince, userIds, cancellationToken);
            var found = await _devices.FindPageAsync(db,
                new PushDeviceQuery(PlatformFilter(request.Platform), userIds, seenSince, (page - 1) * pageSize, pageSize), cancellationToken);
            var names = await _users.GetNamesAsync(db.TenantId, found.Items.Select(d => d.UserId).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);

            var rows = found.Items
                .Select(d => new PushDeviceRow(d.Id, d.UserId, names.GetValueOrDefault(d.UserId) ?? d.UserId, d.Platform, d.Name, d.LastSeenAt))
                .ToList();
            return new PushDeviceList(rows, found.Total, Counts(counts));
        }

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
        {
            if (!NotificationDevice.IsId(id))
                return Task.FromResult(false);

            return _devices.DeleteAsync(_context.TenantDb(), id, cancellationToken);
        }

        /// <summary>Null = no search. Empty = a search that matched nobody.</summary>
        private async Task<IReadOnlyList<string>?> SearchAsync(string tenantId, string? search, CancellationToken cancellationToken)
        {
            var text = search?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;

            return await _users.SearchIdsAsync(tenantId, PushTextRenderer.Cut(text, MaxSearchLength), MaxSearchUsers, cancellationToken);
        }

        /// <summary>No platform: all. An unknown platform matches nothing.</summary>
        private static string? PlatformFilter(string? platform) =>
            string.IsNullOrWhiteSpace(platform) ? null : PushPlatforms.Normalize(platform) ?? NoPlatform;

        private static PushDeviceCounts Counts(IReadOnlyDictionary<string, int> counts)
        {
            var android = counts.GetValueOrDefault(PushPlatforms.Android);
            var ios = counts.GetValueOrDefault(PushPlatforms.Ios);
            var web = counts.GetValueOrDefault(PushPlatforms.Web);
            return new PushDeviceCounts(android + ios + web, android, ios, web);
        }
    }
}
