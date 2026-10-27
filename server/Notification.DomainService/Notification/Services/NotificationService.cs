using Blocks.Genesis;
using DomainService.Configuration.Services;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using StackExchange.Redis;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;

namespace DomainService.Notification
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IValidator<Subscription> _subscriptionValidator;
        private readonly IValidator<NotifyRequest> _notifyRequestValidator;
        private readonly ILogger<NotificationService> _logger;
        private readonly INotifierServiceFactory _notifierFactory;
        private readonly IConfigurationRepository _configurationRepository;
        private readonly INotificationAudienceResolver _audienceResolver;

        public NotificationService(INotificationRepository notificationRepository,
                                   IValidator<Subscription> validator,
                                   IValidator<NotifyRequest> notifyRequestValidator,
                                   ILogger<NotificationService> logger,
                                   INotifierServiceFactory notifierFactory,
                                   IConfigurationRepository configurationRepository,
                                   INotificationAudienceResolver audienceResolver)
        {
            _audienceResolver = audienceResolver;
            _notificationRepository = notificationRepository;
            _subscriptionValidator = validator;
            _notifyRequestValidator = notifyRequestValidator;
            _notifierFactory = notifierFactory;
            _configurationRepository = configurationRepository;
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
                // Said, not thrown: an unknown name used to end in a NullReferenceException (HTTP 500) that
                // told the sender nothing, and the notification was silently lost.
                _logger.LogWarning("Notify: no notification configuration named {ConfigurationName}", notifyRequest.ConfigurationName);
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        [nameof(NotifyRequest.ConfigurationName)] = $"No notification configuration named '{notifyRequest.ConfigurationName}'.",
                    },
                };
            }
            if (configuration.IsDisabledByTenant())
            {
                // Switched off by the tenant: no push and no stored copy. Success, so senders do not retry.
                _logger.LogInformation("Notify: configuration {ConfigurationName} is disabled by the tenant; nothing sent", notifyRequest.ConfigurationName);
                return new BaseResponse { IsSuccess = true };
            }

            var sendConfiguration = await ApplyAudienceAsync(configuration, notifyRequest);
            if (sendConfiguration is null)
            {
                return new NotifyResponse { IsSuccess = true, Message = NotifyResponse.NoReceivers };
            }

            await SendNotificationAsync(sendConfiguration, notifyRequest);

            //TODO
            //await PublishEventForNotification(notifyRequest);

            return new BaseResponse { IsSuccess = true };
        }

        /// <summary>
        /// Applies the configuration audience: the request is narrowed to the final user ids and sent user-specific
        /// through a copy of the configuration. Returns the configuration to send with, or null when no one is left.
        /// Without an audience, and for an everyone-broadcast the sender did not narrow, nothing changes.
        /// </summary>
        private async Task<NotificationConfiguration?> ApplyAudienceAsync(NotificationConfiguration configuration, NotifyRequest notifyRequest)
        {
            var audience = configuration.Audience;
            if (audience is null || !audience.IsSet())
                return configuration;

            if (audience.EveryoneInTenant && !audience.HasRules()
                && configuration.NotificationType == NotificationReceiverTypes.BroadcastReceiverType
                && !SenderNarrowing.SenderGaveLists(notifyRequest))
                return configuration;

            var resolution = await _audienceResolver.ResolveAsync(audience, notifyRequest);
            if (resolution.DroppedRequestedCount > 0)
            {
                // Counts only: user ids never go to the log.
                _logger.LogWarning("Notify: {DroppedCount} requested user(s) are outside the audience of configuration {ConfigurationName}; left out",
                    resolution.DroppedRequestedCount, notifyRequest.ConfigurationName);
            }

            if (resolution.UserIds.Count == 0)
            {
                _logger.LogInformation("Notify: no receivers for configuration {ConfigurationName} (audience {AudienceCount}, final 0); nothing sent",
                    notifyRequest.ConfigurationName, resolution.AudienceCount);
                return null;
            }

            notifyRequest.UserIds = [.. resolution.UserIds];
            notifyRequest.Roles = null;
            notifyRequest.OrganizationIds = null;
            notifyRequest.SubscriptionFilters = null;
            return AsUserSpecific(configuration);
        }

        /// <summary>A copy, so a configuration object the caller or a cache holds is never changed.</summary>
        private static NotificationConfiguration AsUserSpecific(NotificationConfiguration configuration)
        {
            var copy = BsonSerializer.Deserialize<NotificationConfiguration>(configuration.ToBsonDocument());
            copy.NotificationType = NotificationReceiverTypes.UserSpecificReceiverType;
            return copy;
        }

        private async Task SendNotificationAsync(NotificationConfiguration configuration, NotifyRequest notifyRequest)
        {
            var provider = _notifierFactory.GetNotifierServiceProvider(configuration.ChannelToNotify);
            await provider.Notify(notifyRequest, configuration);

            _logger.LogInformation("Notify: Notify request has handled successfully for configuration {ConfigurationName}", notifyRequest.ConfigurationName);
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
    }
}
