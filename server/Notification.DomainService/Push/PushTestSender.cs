namespace DomainService.Notification.Push
{
    /// <summary>Sends one fixed text to one user's devices (push settings test).</summary>
    public interface IPushTestSender
    {
        /// <summary>Pushes sent; null when the user has no device.</summary>
        Task<int?> SendAsync(PushDb db, string userId, PushSettingsRecord settings, string title, string body, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushTestSender"/>
    public sealed class PushTestSender : IPushTestSender
    {
        private readonly IPushDeviceStore _devices;
        private readonly IFcmPushSender _sender;

        public PushTestSender(IPushDeviceStore devices, IFcmPushSender sender)
        {
            _devices = devices;
            _sender = sender;
        }

        public async Task<int?> SendAsync(PushDb db, string userId, PushSettingsRecord settings, string title, string body, CancellationToken cancellationToken)
        {
            var devices = await _devices.GetForUsersAsync(db, [userId], cancellationToken);
            if (devices.Count == 0)
                return null;

            var data = new Dictionary<string, string> { ["test"] = "true" };
            var notes = devices.Select(d => new PushNote(db, d, title, body, data)).ToList();
            var summary = await _sender.SendAsync(db.TenantId, settings, notes, cancellationToken);
            return summary.Sent;
        }
    }
}
