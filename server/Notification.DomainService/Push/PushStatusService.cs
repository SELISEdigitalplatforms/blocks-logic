namespace DomainService.Notification.Push
{
    /// <summary>One platform on the settings screen: devices registered (seen in the last 60 days) and the last test.</summary>
    public sealed record PushPlatformStatus(int Devices, PushLastTest? LastTest);

    /// <summary>Answer of <c>GET Notifier/push-status</c>.</summary>
    public sealed record PushStatusView(PushPlatformStatus Android, PushPlatformStatus Ios, PushPlatformStatus Web);

    /// <summary>Runtime state of push for the settings screen of the context tenant.</summary>
    public interface IPushStatusService
    {
        Task<PushStatusView> GetAsync(CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushStatusService"/>
    /// <remarks>Counts devices in the tenant DB only (never ROOT DB rows, which belong to no single project).</remarks>
    public sealed class PushStatusService : IPushStatusService
    {
        private readonly IPushDeviceService _context;
        private readonly IPushDeviceStore _devices;
        private readonly IPushStatusStore _status;
        private readonly TimeProvider _timeProvider;

        public PushStatusService(IPushDeviceService context, IPushDeviceStore devices, IPushStatusStore status, TimeProvider timeProvider)
        {
            _context = context;
            _devices = devices;
            _status = status;
            _timeProvider = timeProvider;
        }

        public async Task<PushStatusView> GetAsync(CancellationToken cancellationToken)
        {
            var seenSince = NotificationDevice.SeenSince(_timeProvider.GetUtcNow().UtcDateTime);
            var counts = await _devices.CountByPlatformAsync(_context.TenantDb(), seenSince, null, cancellationToken);
            var status = await _status.GetAsync(cancellationToken);

            PushPlatformStatus Of(string platform) => new(counts.GetValueOrDefault(platform), status?.Get(platform));
            return new PushStatusView(Of(PushPlatforms.Android), Of(PushPlatforms.Ios), Of(PushPlatforms.Web));
        }
    }
}
