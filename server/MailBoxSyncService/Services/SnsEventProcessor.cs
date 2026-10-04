using System.Text.Json;
using MailBoxSyncService.Entities;

namespace MailBoxSyncService.Services
{
    public class SnsEventProcessor(IHttpClientFactory httpClientFactory, IMailBoxSyncService mailBoxSyncService, ILogger<SnsEventProcessor> logger) : ISnsEventProcessor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private readonly IMailBoxSyncService _mailBoxSyncService = mailBoxSyncService;
        private readonly ILogger<SnsEventProcessor> _logger = logger;

        public async Task ProcessAsync(HttpRequest request)
        {
            var body = await ReadRequestBodyAsync(request);
            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.LogWarning("SNS received empty body");
                return;
            }

            var snsMessage = Deserialize<SnsMessage>(body);

            if (snsMessage?.Type is null)
                return;

            switch (snsMessage.Type)
            {
                case SnsMessageTypes.SubscriptionConfirmation:
                    await ConfirmSubscriptionAsync(snsMessage);
                    break;

                case SnsMessageTypes.Notification:
                    await ProcessNotificationAsync(snsMessage);
                    break;

                default:
                    _logger.LogWarning("Unknown SNS message type: {Type}", snsMessage.Type);
                    break;
            }
        }

        private async Task ConfirmSubscriptionAsync(SnsMessage message)
        {
            _logger.LogInformation("Confirming SNS subscription: {Url}", message.SubscribeURL);

            var client = _httpClientFactory.CreateClient();
            await client.GetAsync(message.SubscribeURL);
        }

        private async Task ProcessNotificationAsync(SnsMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Message))
            {
                _logger.LogWarning("SNS Notification message is empty");
                return;
            }

            var sesEvent = Deserialize<SesEventNotification>(message.Message);
            if (sesEvent is null)
                return;

            var tenantId = ExtractTenantId(sesEvent);
            if (tenantId is null)
                return;

            await _mailBoxSyncService.SyncOutgoingAsync(sesEvent, tenantId);
        }

        private static string? ExtractTenantId(SesEventNotification sesEvent)
        {
            return sesEvent.Mail?.Headers?.FirstOrDefault(h => h.Name.Equals("X-Tenant-Id", StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static T? Deserialize<T>(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            request.EnableBuffering();

            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            return body;
        }
    }

    public static class SnsMessageTypes
    {
        public const string SubscriptionConfirmation = "SubscriptionConfirmation";
        public const string Notification = "Notification";
    }
}
