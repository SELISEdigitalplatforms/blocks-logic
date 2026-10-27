using DomainService.Configuration;
using DomainService.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using Blocks.Genesis.Entities.Os;
using Blocks.Genesis.Entities.Logic;


namespace DomainService.Notification
{
    public class FirebaseNotificationServiceProvider : INotifier
    {
        private readonly ILogger<FirebaseNotificationServiceProvider> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        private static readonly HttpClient HttpClient = new HttpClient();
        private const string DefaultMediaType = "application/json";

        public FirebaseNotificationServiceProvider(ILogger<FirebaseNotificationServiceProvider> logger,
                                                   INotificationRepository notificationRepository,
                                                   IConfiguration configuration,
                                                   HttpClient? httpClient = null)
        {
            _logger = logger;
            _configuration = configuration;
            _notificationRepository = notificationRepository;
            _httpClient = httpClient ?? HttpClient;
        }


        public async Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            // Same rule as NotificationService.NotifyAsync, so no in-process caller can send a disabled configuration.
            if (configuration.IsDisabledByTenant())
            {
                _logger.LogInformation("Notify: configuration {ConfigurationName} is disabled by the tenant; nothing sent", configuration.Name);
                return;
            }

            if (string.IsNullOrWhiteSpace(notifyRequest.DenormalizedPayload))
            {
                _logger.LogWarning("Firebase: DenormalizedPayload is required");
                await Task.CompletedTask;
            }

            // A filter or role send can arrive with no user ids; that is no one to push to, not a crash.
            var userIds = notifyRequest.UserIds ?? [];
            var config = await _notificationRepository.GetItemAsync<FirebaseConfiguration>(x => true);

            if (config == null)
            {
                _logger.LogWarning("Firebase: Firebase config not found");
                await Task.CompletedTask;
            }

            if (config != null && string.IsNullOrWhiteSpace(config.AuthorizationKey))
            {
                _logger.LogWarning("Firebase: AuthKey not found");
                await Task.CompletedTask;
            }

            var firebaseUrl = _configuration["FirebaseUri"];

            foreach (var userId in userIds)
            {
                if (config == null) return;
                var applicationId = config.AuthorizationKey;

                var data = new
                {
                    to = $"/topics/{userId}",
                    data = JsonConvert.DeserializeObject(notifyRequest.DenormalizedPayload),
                    notification = JsonConvert.DeserializeObject(notifyRequest.DenormalizedPayload),
                    content_available = notifyRequest.ContentAvailable
                };

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, firebaseUrl)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data), Encoding.UTF8, DefaultMediaType)
                };

                requestMessage.Headers.TryAddWithoutValidation("Authorization", $"key={applicationId}");
                requestMessage.Headers.TryAddWithoutValidation("Content-Type", "application/json");

                // Never the payload or the response object: the response carries the request and its
                // "Authorization: key=<server key>" header (user rule 2026-10-08).
                _logger.LogInformation("Sending a notification to Firebase topic for user {UserId}", userId);
                var response = await _httpClient.SendAsync(requestMessage);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Firebase accepted the notification for user {UserId}: {StatusCode}", userId, (int)response.StatusCode);
                    continue;
                }

                var responseStr = await response.Content.ReadAsStringAsync();
                var exception = new Exception(responseStr);

                _logger.LogError("Notify: Failed to send notification using firebase {exception}", exception);
                throw exception;
            }

            _logger.LogDebug("Notify-Firebase: Notification has sent successfully by Firebase");
        }
    }
}
