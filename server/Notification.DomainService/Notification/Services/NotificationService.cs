using Blocks.Genesis;
using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;

namespace DomainService.Notification
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IValidator<Subscription> _subscriptionValidator;
        private readonly IValidator<NotifyRequest> _notifyRequestValidator;
        private readonly IValidator<RegisterWebPushSubscriptionRequest> _registerWebPushValidator;
        private readonly IValidator<UnregisterWebPushSubscriptionRequest> _unregisterWebPushValidator;
        private readonly ILogger<NotificationService> _logger;
        private readonly INotifierServiceFactory _notifierFactory;
        private readonly IConfigurationRepository _configurationRepository;
        private readonly IWebPushVapidKeyService _vapidKeyService;

        public NotificationService(
            INotificationRepository notificationRepository,
            NotificationServiceValidators validators,
            ILogger<NotificationService> logger,
            INotifierServiceFactory notifierFactory,
            IConfigurationRepository configurationRepository,
            IWebPushVapidKeyService vapidKeyService)
        {
            _notificationRepository = notificationRepository;
            _subscriptionValidator = validators.Subscription;
            _notifyRequestValidator = validators.NotifyRequest;
            _registerWebPushValidator = validators.RegisterWebPush;
            _unregisterWebPushValidator = validators.UnregisterWebPush;
            _notifierFactory = notifierFactory;
            _configurationRepository = configurationRepository;
            _vapidKeyService = vapidKeyService;
            _logger = logger;
        }

        public async Task<BaseResponse> AddSubscriptionAsync(Subscription request)
        {
            var validationResult = await _subscriptionValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
            {
                return new BaseResponse { Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            var subscriptionFilters = request.Payload.SubscriptionFilters.Select(subscription => new NotificationSubscription
            {
                Id = Guid.NewGuid().ToString(),
                UserId = request.Payload.UserIds.FirstOrDefault(),
                ConnectionId = request.Payload.ConnectionId,
                ActionName = subscription.ActionName,
                Context = subscription.Context,
                Value = subscription.Value,
                CreatedTime = DateTime.UtcNow
            });

            await _notificationRepository.SaveAsync(subscriptionFilters.ToList());

            return new BaseResponse { IsSuccess = true };

        }

        public async Task CreateConnectionAsync(string connectionId)
        {
            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

            var connection = new NotificationConnection
            {
                ConnectionId = connectionId,
                UserId = !string.IsNullOrWhiteSpace(userId) ? userId : null,
                CreatedTime = DateTime.UtcNow,
                Id = Guid.NewGuid().ToString(),
            };

            await _notificationRepository.SaveAsync(connection);
        }

        public async Task<BaseResponse> NotifyAsync(NotifyRequest notifyRequest)
        {
            var validateResult = await _notifyRequestValidator.ValidateAsync(notifyRequest);

            if (!validateResult.IsValid)
            {
                return new BaseResponse { IsSuccess = false, Errors = validateResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            var configuration = await _configurationRepository.GetByNameAsync(notifyRequest.ConfigurationName);
            if (configuration is null)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "configurationName", "Configuration not found." } }
                };
            }

            try
            {
                await SendNotificationAsync(configuration, notifyRequest);
            }
            catch (WebPushQueueUnavailableException)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { WebPushConstants.QueueErrorKey, WebPushConstants.QueueErrorMessage } }
                };
            }

            //TODO
            //await PublishEventForNotification(notifyRequest);

            return new BaseResponse { IsSuccess = true };
        }

        private async Task SendNotificationAsync(NotificationConfiguration configuration, NotifyRequest notifyRequest)
        {
            var provider = _notifierFactory.GetNotifierServiceProvider(configuration.ChannelToNotify);
            await provider.Notify(notifyRequest, configuration);

            _logger.LogInformation("Notify: Notify request has handled successfully with payload {@payload}", notifyRequest);
        }

        public async Task RemoveCollectionAsync(string collectionId)
        {
            await Task.WhenAll(_notificationRepository.DeleteAsync<NotificationConnection>(p => p.ConnectionId == collectionId),
                               _notificationRepository.DeleteAsync<NotificationSubscription>(p => p.ConnectionId == collectionId));
        }

        public async Task<BaseResponse> RemoveSubscriptionAsync(Subscription subscription)
        {
            var validationResult = await _subscriptionValidator.ValidateAsync(subscription);

            if (!validationResult.IsValid)
            {
                return new BaseResponse { Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            if (subscription?.Payload?.SubscriptionFilters != null)
            {
                foreach (var subscriptionFilter in subscription.Payload.SubscriptionFilters)
                {
                    await _notificationRepository.DeleteAsync<NotificationSubscription>(s =>
                        s.ConnectionId == subscription.Payload.ConnectionId
                        && s.Context == subscriptionFilter.Context
                        && s.ActionName == subscriptionFilter.ActionName
                        && s.Value == subscriptionFilter.Value);
                }
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter(GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            return request.OrderBy switch
            {
                OfflineNotificationOrder.CreatedTime => await GetUnReadNotificationsByUserIdOrderByCreatedDateDesc(request.UserId, request.SubscriptionFilterData),
                OfflineNotificationOrder.ReadStatus => await GetUnReadNotificationsByUserIdOrderByReadStatus(request.UserId, request.SubscriptionFilterData),
                _ => []
            };
        }

        private async Task<List<OfflineNotification>> GetUnReadNotificationsByUserIdOrderByReadStatus(string userId, SubscriptionFilter subscriptionFilterData)
        {
            var offlineNotifications = (await _notificationRepository.GetItemsAsync<OfflineNotification>(p => ((!string.IsNullOrWhiteSpace(p.Payload.UserId) && p.Payload.UserId == userId) || p.Payload.NotificationType == NotificationReceiverTypes.BroadcastReceiverType.ToString())
                                       && p.Payload.SubscriptionFilters.Contains(subscriptionFilterData))).ToList();

            if (offlineNotifications.Any())
            {
                offlineNotifications = offlineNotifications.Select(p => new OfflineNotification
                {
                    Id = p.Id,
                    Payload = p.Payload,
                    CreatedTime = p.CreatedTime,
                    ReadByUserIds = p.ReadByUserIds,
                    IsRead = p.ReadByUserIds?.Contains(userId.ToString()) ?? false,
                    DenormalizedPayload = p.DenormalizedPayload
                }).OrderBy(p => p.IsRead).ToList();
            }

            return offlineNotifications;
        }

        private async Task<List<OfflineNotification>> GetUnReadNotificationsByUserIdOrderByCreatedDateDesc(string userId, SubscriptionFilter subscriptionFilterData)
        {

            var statusList = !string.IsNullOrWhiteSpace(subscriptionFilterData?.Context)
                ? subscriptionFilterData.Context.Split(',')
                : null;
            List<OfflineNotification> offlineNotifications = [];

            offlineNotifications = statusList != null && statusList.Length > 1
                ? await GetNotificationForMultiContext(statusList, userId, subscriptionFilterData)
                : await GetNotificationForSingleContext(userId, subscriptionFilterData);

            var notifications = offlineNotifications.Select(p => new OfflineNotification
            {
                Id = p.Id,
                Payload = p.Payload,
                CreatedTime = p.CreatedTime,
                ReadByUserIds = p.ReadByUserIds,
                IsRead = p.ReadByUserIds?.Contains(userId.ToString()) ?? false,
                DenormalizedPayload = p.DenormalizedPayload
            });

            return notifications.ToList();
        }

        private async Task<List<OfflineNotification>> GetNotificationForMultiContext(string[] statusList, string userId, SubscriptionFilter subscriptionFilterData)
        {
            if (subscriptionFilterData == null)
                return [];

            var statList = statusList.ToList();
            var notificationList = (await _notificationRepository.GetItemsAsync<OfflineNotification>(p =>
                                              ((!string.IsNullOrWhiteSpace(p.Payload.UserId) && p.Payload.UserId == userId) || p.Payload.NotificationType == NotificationReceiverTypes.BroadcastReceiverType.ToString()) &&
                                               p.Payload.SubscriptionFilters.Any(sf =>
                                               statList.Contains(sf.Context) &&
                                               sf.ActionName == subscriptionFilterData.ActionName && sf.Value == subscriptionFilterData.Value))).OrderByDescending(f => f.CreatedTime);

            return [.. notificationList];
        }

        private async Task<List<OfflineNotification>> GetNotificationForSingleContext(string userId, SubscriptionFilter subscriptionFilterData)
        {
            if (subscriptionFilterData == null)
                return [];

            var notificationList = (await _notificationRepository.GetItemsAsync<OfflineNotification>(p =>
                                                    ((!string.IsNullOrWhiteSpace(p.Payload.UserId) && p.Payload.UserId == userId) || p.Payload.NotificationType == NotificationReceiverTypes.BroadcastReceiverType.ToString()) &&
                                                    p.Payload.SubscriptionFilters.Any(sf =>
                                                    sf.ActionName == subscriptionFilterData.ActionName && sf.Value == subscriptionFilterData.Value))).OrderByDescending(p => p.CreatedTime);

            return [.. notificationList];
        }

        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            try
            {
                string userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
                await _notificationRepository.UpdateNotificationAsReadByUserIdAsync(userId);
                return new BaseResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while marking notificationAsRead {errorMessage: } {stackTrace}", ex.Message, ex.StackTrace);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string>() { { "Exception", $" {ex.Message}" } } };
            }
        }

        public async Task<BaseResponse> MarkNotificationAsRead(MarkNotificationAsReadRequest request)
        {
            try
            {
                string userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
                await _notificationRepository.UpdateNotificationAsReadByUserIdAsync(userId, request.Id);
                return new BaseResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while marking notificationAsRead {errorMessage: } {stackTrace}", ex.Message, ex.StackTrace);
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string>() { { "Exception", $" {ex.Message}" } } };
            }
        }

        public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request)
        {
            return await _notificationRepository.GetNotificationsAsync(request);
        }

        public async Task<BaseResponse> RegisterWebPushSubscriptionAsync(RegisterWebPushSubscriptionRequest request)
        {
            var validationResult = await _registerWebPushValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "userId", "User context is required." } }
                };
            }

            await _notificationRepository.DeleteAsync<WebPushSubscription>(
                s => s.UserId == userId && s.Endpoint == request.Endpoint);

            await _notificationRepository.SaveAsync(new WebPushSubscription
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Endpoint = request.Endpoint,
                Keys = new WebPushSubscriptionKeys
                {
                    P256dh = request.Keys.P256dh,
                    Auth = request.Keys.Auth,
                },
                ExpirationTime = request.ExpirationTime,
                UserAgent = request.UserAgent,
                CreatedTime = DateTime.UtcNow,
            });

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> UnregisterWebPushSubscriptionAsync(UnregisterWebPushSubscriptionRequest request)
        {
            var validationResult = await _unregisterWebPushValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await _notificationRepository.DeleteAsync<WebPushSubscription>(
                    s => s.UserId == userId && s.Endpoint == request.Endpoint);
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<GetWebPushPublicKeyResponse> GetWebPushPublicKeyAsync()
        {
            var publicKey = await _vapidKeyService.GetOrCreatePublicKeyAsync();
            return new GetWebPushPublicKeyResponse { PublicKey = publicKey };
        }

        public async Task<BaseResponse> RotateWebPushVapidKeysAsync()
        {
            await _vapidKeyService.RotateAsync();
            await _notificationRepository.DeleteAsync<WebPushSubscription>(_ => true);
            return new BaseResponse { IsSuccess = true };
        }
    }
}
