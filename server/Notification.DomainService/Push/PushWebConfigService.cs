using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>The Firebase web config a browser needs to register for push.</summary>
    public sealed record PushWebConfig(string ApiKey, string AppId, string MessagingSenderId, string VapidKey, string ProjectId);

    /// <summary>Answer of <c>GET Notifier/push-web-config</c>.</summary>
    public sealed class PushWebConfigResponse
    {
        public const string NotSetUpText = "Web push is not set up.";

        public bool IsSuccess { get; init; }
        public PushWebConfig? Data { get; init; }
        public IReadOnlyDictionary<string, string>? Errors { get; init; }

        public static PushWebConfigResponse NotSetUp { get; } = new()
        {
            IsSuccess = false,
            Errors = new Dictionary<string, string> { ["web"] = NotSetUpText },
        };
    }

    /// <summary>Gives the web config to signed-in web apps of the context tenant.</summary>
    public interface IPushWebConfigService
    {
        Task<PushWebConfigResponse> GetAsync(CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushWebConfigService"/>
    /// <remarks>
    /// Only for a signed-in user with a user id; only the context tenant's own config; only when push and Web are on
    /// and a web config is stored. Every other case answers the same "not set up" (no hint which part is missing).
    /// The value is never logged.
    /// </remarks>
    public sealed class PushWebConfigService : IPushWebConfigService
    {
        private const int MaxJsonBytes = 4 * 1024;

        private readonly IPushSettingsStore _settings;
        private readonly IPushCredentialStore _credentials;
        private readonly ILogger<PushWebConfigService> _logger;

        public PushWebConfigService(IPushSettingsStore settings, IPushCredentialStore credentials, ILogger<PushWebConfigService> logger)
        {
            _settings = settings;
            _credentials = credentials;
            _logger = logger;
        }

        public async Task<PushWebConfigResponse> GetAsync(CancellationToken cancellationToken)
        {
            var context = BlocksContext.GetContext();
            if (context is null || !context.IsAuthenticated || string.IsNullOrWhiteSpace(context.UserId) || string.IsNullOrWhiteSpace(context.TenantId))
                return PushWebConfigResponse.NotSetUp;

            var settings = await _settings.GetAsync(cancellationToken);
            if (settings is not { CanSend: true } || !settings.IsOn(PushPlatforms.Web))
                return PushWebConfigResponse.NotSetUp;

            var json = await ReadAsync(context.TenantId, settings, cancellationToken);
            var config = Parse(json, settings.ProjectId);
            return config is null ? PushWebConfigResponse.NotSetUp : new PushWebConfigResponse { IsSuccess = true, Data = config };
        }

        private async Task<string?> ReadAsync(string tenantId, PushSettingsRecord settings, CancellationToken cancellationToken)
        {
            try
            {
                return await _credentials.ReadAsync(tenantId, settings.WebSecretId, settings.WebSecretName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Type only: never the value, and a store message can name the secret.
                _logger.LogWarning("Push: the web config could not be read ({ErrorType})", ex.GetType().Name); // NOSONAR S6667 — type only
                return null;
            }
        }

        /// <summary>The four fields, each a non-empty string; null for anything else.</summary>
        internal static PushWebConfig? Parse(string? json, string projectId)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
                return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                var apiKey = Text(root, "apiKey");
                var appId = Text(root, "appId");
                var senderId = Text(root, "messagingSenderId");
                var vapidKey = Text(root, "vapidKey");
                return apiKey is null || appId is null || senderId is null || vapidKey is null
                    ? null
                    : new PushWebConfig(apiKey, appId, senderId, vapidKey, projectId);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;
    }
}
