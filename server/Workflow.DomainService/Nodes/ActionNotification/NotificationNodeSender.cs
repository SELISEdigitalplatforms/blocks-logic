using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;

namespace Workflow.DomainService.Nodes.ActionNotification
{
    /// <summary>The Notifier request the Send Notification step builds (the API's <c>NotifyRequest</c> shape).</summary>
    public sealed class NotificationNodeRequest
    {
        public string ConfigurationName { get; init; } = string.Empty;
        public List<string> UserIds { get; init; } = new();
        public List<string> Roles { get; init; } = new();
        public List<string> OrganizationIds { get; init; } = new();
        public List<ActionNotificationFilter> SubscriptionFilters { get; init; } = new();
        public string DenormalizedPayload { get; init; } = string.Empty;
        public bool SaveDenormalizedPayloadAsAnObject { get; init; }
        public string ResponseKey { get; init; } = string.Empty;
        public string ResponseValue { get; init; } = string.Empty;
        public bool ContentAvailable { get; init; } = true;
        public string ConnectionId { get; init; } = string.Empty;
    }

    /// <summary>What the Notifier answered. <see cref="Error"/> is set when <see cref="Success"/> is false.</summary>
    public readonly record struct NotificationNodeSendResult(bool Success, string? Error);

    /// <summary>Sends a Send Notification step's request to the Notifier.</summary>
    public interface INotificationNodeSender
    {
        Task<NotificationNodeSendResult> SendAsync(string tenantId, NotificationNodeRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// POSTs to <c>NotificationServiceUrl</c> (<c>/api/Notifier/Notify</c>) signed as the workflow's own project
    /// (<c>x-blocks-key</c> = tenant id, <c>Secret</c> = hash of the tenant id with its salt), so the Notifier
    /// reads that project's configurations and a broadcast stays in that project. Over HTTP, not in-process:
    /// steps run in the Worker, which has no SignalR backplane, so an in-process push would reach no browser.
    /// Never throws for a failed send; never logs the request (the payload can hold anything).
    /// </summary>
    public sealed class NotificationNodeSender : INotificationNodeSender
    {
        internal const string UrlSetting = "NotificationServiceUrl";
        internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IConfiguration _configuration;
        private readonly ITenants _tenants;
        private readonly ICryptoService _crypto;
        private readonly IHttpClientFactory _httpClientFactory;

        public NotificationNodeSender(IConfiguration configuration, ITenants tenants, ICryptoService crypto, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _tenants = tenants;
            _crypto = crypto;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<NotificationNodeSendResult> SendAsync(string tenantId, NotificationNodeRequest request, CancellationToken cancellationToken)
        {
            var url = _configuration[UrlSetting];
            if (string.IsNullOrWhiteSpace(url))
                return new(false, $"{UrlSetting} is not configured.");

            var salt = _tenants.GetTenantByID(tenantId)?.TenantSalt;
            if (string.IsNullOrWhiteSpace(salt))
                return new(false, "The project could not be found to sign the notification.");

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(SendTimeout);

                using var message = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json"),
                };
                message.Headers.TryAddWithoutValidation("x-blocks-key", tenantId);
                message.Headers.TryAddWithoutValidation("Secret", _crypto.Hash(tenantId, salt));

                using var response = await _httpClientFactory.CreateClient().SendAsync(message, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);

                if (!response.IsSuccessStatusCode)
                    return new(false, $"The Notifier answered HTTP {(int)response.StatusCode}.");

                return ReadAnswer(body);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, $"The Notifier did not answer within {SendTimeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException ex)
            {
                // Type only: the message can carry the URL.
                return new(false, $"The Notifier could not be reached ({ex.GetType().Name}).");
            }
        }

        /// <summary>Reads <c>{ isSuccess, errors }</c>. Error values are the Notifier's own validation texts.</summary>
        internal static NotificationNodeSendResult ReadAnswer(string body)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new(false, "The Notifier answer could not be read.");

                if (TryGet(root, "isSuccess", out var ok) && ok.ValueKind == JsonValueKind.True)
                    return new(true, null);

                if (TryGet(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var texts = errors.EnumerateObject()
                        .Select(p => p.Value.ValueKind == JsonValueKind.String ? $"{p.Name}: {p.Value.GetString()}" : p.Name)
                        .ToList();
                    if (texts.Count > 0)
                        return new(false, string.Join("; ", texts));
                }

                return new(false, "The Notifier did not accept the notification.");
            }
            catch (JsonException)
            {
                return new(false, "The Notifier answer could not be read.");
            }
        }

        private static bool TryGet(JsonElement root, string name, out JsonElement value)
        {
            foreach (var property in root.EnumerateObject()
                         .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }

            value = default;
            return false;
        }
    }
}
