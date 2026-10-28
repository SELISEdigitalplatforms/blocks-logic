using Blocks.Genesis;
using DomainService.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification
{
    /// <summary>Hub connections and subscriptions, and the caller's inbox (read and mark as read).</summary>
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IValidator<Subscription> _subscriptionValidator;
        private readonly INotificationUserRepository _users;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(INotificationRepository notificationRepository,
                                   IValidator<Subscription> validator,
                                   INotificationUserRepository users,
                                   ILogger<NotificationService> logger)
        {
            _notificationRepository = notificationRepository;
            _subscriptionValidator = validator;
            _users = users;
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

        /// <summary>
        /// The caller's rows matching a subscription filter. The caller is always the token user: a different
        /// <see cref="GetUnreadNotificationsRequestBySubscriptionFilter.UserId"/> is ignored and logged.
        /// </summary>
        public async Task<List<OfflineNotification>> GetUnreadNotificationsBySubscriptionFilter(GetUnreadNotificationsRequestBySubscriptionFilter request)
        {
            var filterData = request?.SubscriptionFilterData;
            if (request is null || filterData is null)
                return [];

            var viewer = await ViewerAsync();
            if (!string.IsNullOrWhiteSpace(request.UserId) && request.UserId != viewer.UserId)
            {
                _logger.LogWarning("GetUnreadNotificationsBySubscriptionFilter named another user id; the caller's own user id is used");
            }

            var visible = InboxFilters.Visible(viewer, includeLegacyBroadcast: true);
            return request.OrderBy switch
            {
                OfflineNotificationOrder.CreatedTime => await ByCreatedTimeAsync(visible, filterData, viewer.UserId),
                OfflineNotificationOrder.ReadStatus => await ByReadStatusAsync(visible, filterData, viewer.UserId),
                _ => []
            };
        }

        private async Task<List<OfflineNotification>> ByReadStatusAsync(FilterDefinition<OfflineNotification> visible,
            SubscriptionFilter filterData, string userId)
        {
            var filter = visible & Builders<OfflineNotification>.Filter.Where(p => p.Payload.SubscriptionFilters.Contains(filterData));
            var rows = await _notificationRepository.GetInboxItemsAsync(filter);
            return [.. InboxRows.ForViewer(rows, userId).OrderBy(p => p.IsRead)];
        }

        private async Task<List<OfflineNotification>> ByCreatedTimeAsync(FilterDefinition<OfflineNotification> visible,
            SubscriptionFilter filterData, string userId)
        {
            var contexts = !string.IsNullOrWhiteSpace(filterData.Context) ? filterData.Context.Split(',').ToList() : null;
            var match = contexts is { Count: > 1 }
                ? Builders<OfflineNotification>.Filter.Where(p => p.Payload.SubscriptionFilters.Any(sf =>
                    contexts.Contains(sf.Context) && sf.ActionName == filterData.ActionName && sf.Value == filterData.Value))
                : Builders<OfflineNotification>.Filter.Where(p => p.Payload.SubscriptionFilters.Any(sf =>
                    sf.ActionName == filterData.ActionName && sf.Value == filterData.Value));

            var rows = await _notificationRepository.GetInboxItemsAsync(visible & match);
            return [.. InboxRows.ForViewer(rows, userId).OrderByDescending(p => p.CreatedTime)];
        }

        public async Task<BaseResponse> MarkAllNotificationAsRead()
        {
            try
            {
                await _notificationRepository.MarkAllAsReadAsync(await ViewerAsync());
                return new BaseResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                return MarkFailed(ex);
            }
        }

        public async Task<BaseResponse> MarkNotificationAsRead(MarkNotificationAsReadRequest request)
        {
            try
            {
                await _notificationRepository.MarkAsReadAsync(await ViewerAsync(), request.Id);
                return new BaseResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                return MarkFailed(ex);
            }
        }

        public async Task<GetNotificationsResponse> GetNotificationsAsync(GetNotificationsRequest request)
        {
            return await _notificationRepository.GetNotificationsAsync(request, await ViewerAsync());
        }

        private BaseResponse MarkFailed(Exception ex)
        {
            // Type only: a driver message can carry ids.
            _logger.LogError("Error while marking notifications as read ({ErrorType})", ex.GetType().Name); // NOSONAR S6667 — type only
            return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "Exception", "Marking as read failed." } } };
        }

        /// <summary>
        /// The token user, the organizations they belong to (organization rows), and for an impersonated root user the
        /// project they work in. No user: refused, never "every row without a user".
        /// </summary>
        private async Task<InboxViewer> ViewerAsync()
        {
            var context = BlocksContext.GetContext();
            var userId = context?.UserId;
            if (string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException("User context is required to read notifications.");

            var organizationIds = await _users.GetOrganizationIdsAsync(userId) ?? [];
            return new InboxViewer(userId, organizationIds, context!.Impersonated ? context.TenantId : null);
        }
    }
}
