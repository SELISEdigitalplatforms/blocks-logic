using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>One test send: whose devices, which platforms, what text and options.</summary>
    public sealed record PushTestRun(PushSettingsRecord Settings, PushDb Db, string UserId, IReadOnlyCollection<string> Platforms,
        PushText Text, PushPlatformBlocks? Blocks);

    /// <summary>Sends a test push to one user's own devices and reports per device.</summary>
    public interface IPushTestRunner
    {
        Task<PushTestResult> RunAsync(PushTestRun run, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushTestRunner"/>
    /// <remarks>Only the devices of <see cref="PushTestRun.UserId"/> in its own DB. The outcome per platform is kept as
    /// the platform's last test (KV <c>firebase-push-status</c>).</remarks>
    public sealed class PushTestRunner : IPushTestRunner
    {
        internal const string KeyUnreadable = "The push key could not be read. Try again.";
        private static readonly IReadOnlyDictionary<string, string> TestData = new Dictionary<string, string> { ["test"] = "true" };

        private readonly IPushDeviceStore _devices;
        private readonly IFcmPushSender _sender;
        private readonly IPushStatusStore _status;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PushTestRunner> _logger;

        public PushTestRunner(IPushDeviceStore devices, IFcmPushSender sender, IPushStatusStore status, TimeProvider timeProvider,
            ILogger<PushTestRunner> logger)
        {
            _devices = devices;
            _sender = sender;
            _status = status;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public Task<PushTestResult> RunAsync(PushTestRun run, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(run);
            return RunCoreAsync(run, cancellationToken);
        }

        private async Task<PushTestResult> RunCoreAsync(PushTestRun run, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var devices = (await _devices.GetForUsersAsync(run.Db, [run.UserId], NotificationDevice.SeenSince(now), cancellationToken))
                .Where(d => run.Platforms.Contains(d.Platform))
                .ToList();
            if (devices.Count == 0)
                return PushTestResult.Refused(PushTestResult.DevicesField, PushTestResult.NoDevice);

            FcmOpening opening;
            try
            {
                opening = await _sender.OpenAsync(run.Db.TenantId, run.Settings, cancellationToken);
            }
            catch (PushKeyUnavailableException)
            {
                // Already logged by type in the sender.
                return PushTestResult.Refused(PushTestResult.PushField, KeyUnreadable);
            }

            if (opening.Failure == FcmOpenFailure.NoKey)
                return PushTestResult.Refused(PushTestResult.PushField, PushTestResult.PushOff);

            var rows = opening.Session is null
                ? devices.Select(d => Row(d, PushTestCodes.KeyRefused, removed: false)).ToList()
                : await SendAsync(opening.Session, run, devices, cancellationToken);

            await RememberAsync(rows, now, cancellationToken);
            return new PushTestResult
            {
                IsSuccess = true,
                SentAt = now,
                Delivered = rows.Count(r => r.Result == PushTestCodes.Delivered),
                Failed = rows.Count(r => r.Result != PushTestCodes.Delivered),
                Devices = rows,
            };
        }

        private async Task<List<PushTestDevice>> SendAsync(FcmSession session, PushTestRun run, List<NotificationDevice> devices,
            CancellationToken cancellationToken)
        {
            var notes = devices.Select(d => PushNotes.Create(run.Db, d, run.Text, TestData, run.Blocks)).ToList();
            var summary = await _sender.SendAsync(session, notes, cancellationToken);
            _logger.LogInformation("Push test: {Sent} sent, {Removed} devices removed, {Failed} failed", summary.Sent, summary.Removed, summary.Failed);
            return summary.Results
                .Select(r => Row(r.Note.Device, PushTestCodes.For(r.Note.Device.Platform, r.Result), r.Removed))
                .ToList();
        }

        /// <summary>The last test of each platform tested; the reason is the first failure code.</summary>
        private async Task RememberAsync(IReadOnlyList<PushTestDevice> rows, DateTime at, CancellationToken cancellationToken)
        {
            var record = await _status.GetAsync(cancellationToken) ?? new PushStatusRecord();
            foreach (var platform in rows.GroupBy(r => r.Platform, StringComparer.Ordinal))
            {
                record.Set(platform.Key, new PushLastTest
                {
                    At = at,
                    Delivered = platform.Count(r => r.Result == PushTestCodes.Delivered),
                    Failed = platform.Count(r => r.Result != PushTestCodes.Delivered),
                    Reason = platform.Select(r => r.Result).FirstOrDefault(code => code != PushTestCodes.Delivered),
                });
            }

            await _status.SaveAsync(record, cancellationToken);
        }

        private static PushTestDevice Row(NotificationDevice device, string result, bool removed) =>
            new(device.Platform, device.Name, device.LastSeenAt, result, removed);
    }
}
