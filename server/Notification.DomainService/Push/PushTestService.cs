namespace DomainService.Notification.Push
{
    /// <summary>Body of <c>POST Notifier/push-test</c>. Null platform = every platform that is on.</summary>
    public sealed class PushTestRequest
    {
        public string? Platform { get; set; }
    }

    /// <summary>
    /// Body of <c>POST Notifier/push-test/notification</c>: an unsaved draft of a notification's push (screen 4).
    /// Sent with sample data: every placeholder is filled with its own field name.
    /// </summary>
    public sealed class PushNotificationTestRequest
    {
        public string? TitleKey { get; set; }
        public string? BodyKey { get; set; }
        public string? Link { get; set; }
        public string? WebLink { get; set; }
        public List<string>? Platforms { get; set; }
        public PushTestOptions? Options { get; set; }
    }

    /// <summary>Per-platform options of a draft (same fields as the stored options).</summary>
    public sealed class PushTestOptions
    {
        public AndroidPushOptions? Android { get; set; }
        public IosPushOptions? Ios { get; set; }
        public WebPushOptions? Web { get; set; }
    }

    /// <summary>Test pushes to the caller's own devices.</summary>
    public interface IPushTestService
    {
        Task<PushTestResult> SendTestAsync(PushTestRequest request, CancellationToken cancellationToken);
        Task<PushTestResult> SendNotificationTestAsync(PushNotificationTestRequest request, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushTestService"/>
    /// <remarks>Never another user's devices: the user comes from the caller's context, not from the request.</remarks>
    public sealed class PushTestService : IPushTestService
    {
        public const int MaxKeyLength = 200;
        internal const string TestTitle = "Test notification";
        internal const string TestBody = "Mobile push works for this project.";
        private const string PlatformChoice = "Platform must be android, ios or web.";

        private readonly IPushSettingsStore _settings;
        private readonly IPushDeviceService _devices;
        private readonly IPushTestRunner _runner;
        private readonly IPushLocalizationRepository _localization;

        public PushTestService(IPushSettingsStore settings, IPushDeviceService devices, IPushTestRunner runner, IPushLocalizationRepository localization)
        {
            _settings = settings;
            _devices = devices;
            _runner = runner;
            _localization = localization;
        }

        public Task<PushTestResult> SendTestAsync(PushTestRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            string[] wanted;
            if (request.Platform is null)
                wanted = [.. PushPlatforms.All];
            else if (PushPlatforms.Normalize(request.Platform) is { } platform)
                wanted = [platform];
            else
                return Task.FromResult(PushTestResult.Refused(PushTestResult.PlatformField, PlatformChoice));

            return SendTestCoreAsync(wanted, cancellationToken);
        }

        private async Task<PushTestResult> SendTestCoreAsync(IReadOnlyCollection<string> wanted, CancellationToken cancellationToken)
        {
            var (settings, platforms, refusal) = await PlatformsOnAsync(wanted, cancellationToken);
            if (refusal is not null)
                return refusal;

            var (db, userId) = _devices.Caller();
            return await _runner.RunAsync(new PushTestRun(settings!, db, userId, platforms, new PushText(TestTitle, TestBody, null), null), cancellationToken);
        }

        public Task<PushTestResult> SendNotificationTestAsync(PushNotificationTestRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var options = new PushNotificationOptions
            {
                Platforms = request.Platforms,
                WebLink = request.WebLink,
                Android = request.Options?.Android,
                Ios = request.Options?.Ios,
                Web = request.Options?.Web,
            };
            var errors = Check(request, options);
            return errors.Count > 0
                ? Task.FromResult(PushTestResult.Refused(errors))
                : SendNotificationCoreAsync(request, options, cancellationToken);
        }

        private async Task<PushTestResult> SendNotificationCoreAsync(PushNotificationTestRequest request, PushNotificationOptions options,
            CancellationToken cancellationToken)
        {
            var wanted = PushPlatforms.All.Where(p => PushNotificationOptions.Allows(options.Platforms, p)).ToList();
            var (settings, platforms, refusal) = await PlatformsOnAsync(wanted, cancellationToken);
            if (refusal is not null)
                return refusal;

            var (db, userId) = _devices.Caller();
            var titleKey = request.TitleKey!.Trim();
            var bodyKey = request.BodyKey!.Trim();
            var texts = await _localization.GetTextsAsync(db.TenantId, [titleKey, bodyKey], cancellationToken);
            var title = PushTextRenderer.Pick(texts.ByKey.GetValueOrDefault(titleKey), null, texts.DefaultLanguage, titleKey);
            var body = PushTextRenderer.Pick(texts.ByKey.GetValueOrDefault(bodyKey), null, texts.DefaultLanguage, bodyKey);

            var fields = PushTextRenderer.SampleFields([title, body, request.Link, request.WebLink, options.Android?.CollapseKey, options.Ios?.ThreadId]);
            var link = string.IsNullOrWhiteSpace(request.Link)
                ? null
                : PushTextRenderer.Fill(request.Link.Trim(), fields, PushTextRenderer.MaxLinkLength, escapeValues: true);
            var text = new PushText(
                PushTextRenderer.Fill(title, fields, PushTextRenderer.MaxTitleLength),
                PushTextRenderer.Fill(body, fields, PushTextRenderer.MaxBodyLength),
                link);
            return await _runner.RunAsync(new PushTestRun(settings!, db, userId, platforms, text, PushOptionRules.Resolve(options, fields)), cancellationToken);
        }

        /// <summary>The settings and the wanted platforms that are on, or why nothing can be sent.</summary>
        private async Task<(PushSettingsRecord? Settings, IReadOnlyCollection<string> Platforms, PushTestResult? Refusal)> PlatformsOnAsync(
            IReadOnlyCollection<string> wanted, CancellationToken cancellationToken)
        {
            var settings = await _settings.GetAsync(cancellationToken);
            if (settings is not { CanSend: true })
                return (null, [], PushTestResult.Refused(PushTestResult.PushField, PushTestResult.PushOff));

            var on = wanted.Where(settings.IsOn).ToList();
            return on.Count == 0
                ? (null, [], PushTestResult.Refused(PushTestResult.PlatformField, PushTestResult.PlatformOff))
                : (settings, on, null);
        }

        private static Dictionary<string, string> Check(PushNotificationTestRequest request, PushNotificationOptions options)
        {
            var errors = PushOptionRules.Check(options);
            if (string.IsNullOrWhiteSpace(request.TitleKey) || request.TitleKey.Trim().Length > MaxKeyLength)
                errors["titleKey"] = "Title key is required (at most 200 characters).";
            if (string.IsNullOrWhiteSpace(request.BodyKey) || request.BodyKey.Trim().Length > MaxKeyLength)
                errors["bodyKey"] = "Message key is required (at most 200 characters).";
            if (request.Link?.Trim().Length > PushOptionRules.MaxLinkLength)
                errors["link"] = "App link is longer than 500 characters.";
            return errors;
        }
    }
}
