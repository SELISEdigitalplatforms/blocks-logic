using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using DomainService.Entities;

namespace DomainService.Notification
{
    public class SignalRNotificationServiceProvider : INotifier
    {
        private readonly IStrategicClientProviderFactory _clientFactoryProvider;
        private readonly INotificationRepository _notificationRepository;
        private readonly ILogger<SignalRNotificationServiceProvider> _logger;
        private const string _usersCollection = "Users";
        public SignalRNotificationServiceProvider(IStrategicClientProviderFactory clientFactoryProvider, 
                                                  INotificationRepository notificationRepository,
                                                  ILogger<SignalRNotificationServiceProvider> logger)
        {
            _clientFactoryProvider = clientFactoryProvider;
            _notificationRepository = notificationRepository;
            _logger = logger;
        }

        public async Task Notify(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            var clientFactoryProvider = _clientFactoryProvider.GetStrategicClientProvider(configuration.NotificationType);
            var clients = await clientFactoryProvider.GetClientAsync(notifyRequest);

            await clients.SendAsync(configuration.NotifyMethod, notifyRequest);

            if (configuration.EnablePersistence)
                await PersistNotificationAsync(notifyRequest, configuration);
        }
        
        private async Task PersistNotificationAsync(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            await InjectUserIdsFromSubscriptionFilter(notifyRequest, configuration);
            var offlineNotifications = await BuildOfflineNotification(notifyRequest, configuration);
            //TODO: ToBeDetermined
            //AppendDenormalizedPayloadIfAny(notifyRequest, offlineNotifications);

            var splitSize = 1500;

            if (offlineNotifications.Count > splitSize)
            {
                var count = offlineNotifications.Count;
                var pageIndex = 0;

                while (count > 0)
                {
                    var tempList = offlineNotifications.Skip(pageIndex++ * splitSize).Take(splitSize).ToList();
                    await _notificationRepository.SaveAsync(tempList);
                    count -= splitSize;
                }
            }
            else
              await  _notificationRepository.SaveAsync(offlineNotifications);

            _logger.LogInformation("Notify-SaveNotifierHandler: Notification payload has saved successfully");
        }

        private async Task InjectUserIdsFromSubscriptionFilter(NotifyRequest notifyRequest, NotificationConfiguration configuration)
        {
            var userIds = new List<string>();

            if (configuration.EnablePersistence && notifyRequest?.SubscriptionFilters != null)
            {
                foreach (var subscriptionFilter in notifyRequest.SubscriptionFilters)
                {
                    var filterUserIds = await GetUserIdsBySubscriptionFilter(subscriptionFilter);
                    foreach (var userId in filterUserIds)
                    {
                        if (userId != null)
                            userIds.Add(userId);
                    }
                }
            }
               
            if (userIds.Count > 0)
                notifyRequest.UserIds = userIds;
        }

        private async Task<List<string?>> GetUserIdsBySubscriptionFilter(SubscriptionFilter subscriptionFilter)
        {
            var subscriptions = ( await _notificationRepository.GetItemsAsync<NotificationSubscription>(p => p.CreatedTime >= DateTime.Now.AddHours(-3).ToUniversalTime() &&((p.Context == subscriptionFilter.Context && p.ActionName == subscriptionFilter.ActionName && p.Value == subscriptionFilter.Value) || (p.Context == subscriptionFilter.Context && p.ActionName == subscriptionFilter.ActionName && p.Value == "") || (p.Context == subscriptionFilter.Context && p.ActionName == "" && p.Value == "")))).Select(p => p.UserId).Distinct();
            return subscriptions.ToList();
        }

        private async Task<List<OfflineNotification>> BuildOfflineNotification(NotifyRequest saveNotification, NotificationConfiguration configuration, List<string>? userIds = null)
        {
            var hasUserIds = saveNotification.UserIds != null && saveNotification.UserIds.Count != 0;
            var hasRoles = saveNotification.Roles != null && saveNotification.Roles.Count != 0;
            var hasOrganizationIds = saveNotification.OrganizationIds != null && saveNotification.OrganizationIds.Count != 0;
            if (hasRoles)
            {
                var roleUserIds = await GetUserIdsByRolesAsync(saveNotification.Roles, saveNotification.OrganizationIds);
                if (roleUserIds.Count != 0)
                {
                    saveNotification.UserIds = roleUserIds;
                }
            }
            else if (!hasUserIds && hasOrganizationIds)
            {
                var organizationUserIds = await GetUserIdsByOrganizationsAsync(saveNotification.OrganizationIds);
                if (organizationUserIds.Count != 0)
                {
                    saveNotification.UserIds = organizationUserIds;
                }
            }
            object? payloadAsObject = null;
            userIds = saveNotification.UserIds ?? [];

            if (saveNotification.SaveDenormalizedPayloadAsAnObject && !string.IsNullOrEmpty(saveNotification.DenormalizedPayload))
            {
                BsonDocument doc;
                BsonDocument.TryParse(saveNotification.DenormalizedPayload, out doc);
                payloadAsObject = BsonSerializer.Deserialize<object>(doc?? new BsonDocument());
            }

            string correlationId = Guid.NewGuid().ToString();

            return userIds.Select(userId =>
                new OfflineNotification()
                {
                    Id = Guid.NewGuid().ToString(),
                    CorrelationId = correlationId,
                    CreatedTime = DateTime.UtcNow,
                    Payload = new PayloadData
                    {
                        NotificationType = configuration.NotificationType.ToString(),
                        ResponseKey = saveNotification.ResponseKey,
                        ResponseValue = saveNotification.ResponseValue,
                        UserId = userId,
                        SubscriptionFilters = saveNotification.SubscriptionFilters
                    },
                    DenormalizedPayload = payloadAsObject ?? saveNotification.DenormalizedPayload
                }).ToList();
        }
        private async Task<List<string>> GetUserIdsByRolesAsync ( List<string> roles, List<string> organizationIds )
        {
            var hasOrganizationFilter = organizationIds != null && organizationIds.Count != 0;
            var users = await GetUsersByOrganizationsAsync(organizationIds);

            return users
                .Where(u => u.Roles != null && u.Roles.Any(orgRoles =>
                    ( !hasOrganizationFilter || organizationIds.Contains(orgRoles.Key) ) &&
                    orgRoles.Value != null && orgRoles.Value.Any(role => roles.Contains(role))))
                .Select(u => u.ItemId)
                .Distinct()
                .ToList();
        }

        private async Task<List<string>> GetUserIdsByOrganizationsAsync ( List<string> organizationIds )
        {
            var users = await GetUsersByOrganizationsAsync(organizationIds);
            return users.Select(u => u.ItemId).Distinct().ToList();
        }
        private async Task<List<NotificationUser>> GetUsersByOrganizationsAsync ( List<string> organizationIds )
        {
            var hasOrganizationFilter = organizationIds != null && organizationIds.Count != 0;

            return await _notificationRepository.GetItemsAsync<NotificationUser>(
                u => !hasOrganizationFilter || u.OrganizationIds.Any(organizationId => organizationIds.Contains(organizationId)),
                _usersCollection);
        }

    }
}
