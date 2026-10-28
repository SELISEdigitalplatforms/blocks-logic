using Blocks.Genesis.Entities.Os;
using Blocks.Genesis.Notifications;
using DomainService.Configuration.Services;

namespace DomainService.Notification.Push
{
    /// <summary>The push text of one notification for each receiver, in the receiver's language.</summary>
    public sealed class PushComposition
    {
        private readonly NotificationPushTemplate _template;
        private readonly PushKeyTexts _texts;
        private readonly IReadOnlyDictionary<string, string> _languages;
        private readonly IReadOnlyDictionary<string, string> _fields;

        public PushComposition(NotificationPushTemplate template, PushKeyTexts texts, IReadOnlyDictionary<string, string> languages,
            IReadOnlyDictionary<string, string> fields)
        {
            _template = template;
            _texts = texts;
            _languages = languages;
            _fields = fields;
        }

        public (string Title, string Body, string? Link) For(string userId)
        {
            var language = _languages.TryGetValue(userId, out var own) ? own : null;
            var title = PushTextRenderer.Pick(_texts.ByKey.GetValueOrDefault(_template.TitleKey), language, _texts.DefaultLanguage, _template.TitleKey);
            var body = PushTextRenderer.Pick(_texts.ByKey.GetValueOrDefault(_template.BodyKey), language, _texts.DefaultLanguage, _template.BodyKey);
            var link = string.IsNullOrWhiteSpace(_template.Link)
                ? null
                : PushTextRenderer.Fill(_template.Link, _fields, PushTextRenderer.MaxLinkLength, escapeValues: true);

            return (PushTextRenderer.Fill(title, _fields, PushTextRenderer.MaxTitleLength),
                PushTextRenderer.Fill(body, _fields, PushTextRenderer.MaxBodyLength),
                link);
        }
    }

    /// <summary>Loads what a push needs besides devices: the configuration's template, its texts and the receivers' languages.</summary>
    public interface IPushMessageComposer
    {
        /// <summary>Null when the configuration is missing or has no push template. Runs in the tenant's context.</summary>
        Task<PushComposition?> PrepareAsync(PushRequested message, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushMessageComposer"/>
    public sealed class PushMessageComposer : IPushMessageComposer
    {
        private readonly IConfigurationRepository _configurations;
        private readonly IPushLocalizationRepository _localization;
        private readonly INotificationUserRepository _users;

        public PushMessageComposer(IConfigurationRepository configurations, IPushLocalizationRepository localization, INotificationUserRepository users)
        {
            _configurations = configurations;
            _localization = localization;
            _users = users;
        }

        public async Task<PushComposition?> PrepareAsync(PushRequested message, CancellationToken cancellationToken)
        {
            var configuration = await _configurations.GetByNameAsync(message.ConfigName);
            var template = configuration?.PushTemplate;
            if (template is null || string.IsNullOrWhiteSpace(template.TitleKey) || string.IsNullOrWhiteSpace(template.BodyKey))
                return null;

            var texts = await _localization.GetTextsAsync(message.TenantId, [template.TitleKey, template.BodyKey], cancellationToken);
            var languages = await LanguagesAsync(message, cancellationToken);
            return new PushComposition(template, texts, languages, PushTextRenderer.FieldsOf(message.Payload));
        }

        private async Task<IReadOnlyDictionary<string, string>> LanguagesAsync(PushRequested message, CancellationToken cancellationToken)
        {
            var rootIds = message.RootUserIds.ToHashSet(StringComparer.Ordinal);
            var projectIds = message.UserIds.Where(id => !rootIds.Contains(id)).ToList();

            var project = await _users.GetLanguagesAsync(message.TenantId, projectIds, rootDb: false, cancellationToken);
            var root = await _users.GetLanguagesAsync(message.TenantId, rootIds, rootDb: true, cancellationToken);
            return project.Concat(root).GroupBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        }
    }
}
