using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    /// <summary>
    /// What genesis <see cref="NotificationHub"/> (mapped at <c>/api/notificationHub</c>) does on each hook. Every
    /// connection joins its tenant group(s), its user group(s) and one group per organization of the user
    /// (<see cref="NotificationHubGroups"/>); genesis senders reach users only through these groups. Subscriptions
    /// are always stored for the caller's own user.
    /// </summary>
    public sealed class NotificationHubHandler : INotificationHubHandler
    {
        private readonly INotificationService _notificationService;
        private readonly INotificationUserRepository _users;
        private readonly ILogger<NotificationHubHandler> _logger;

        public NotificationHubHandler(INotificationService notificationService, INotificationUserRepository users,
            ILogger<NotificationHubHandler> logger)
        {
            _notificationService = notificationService;
            _users = users;
            _logger = logger;
        }

        public Task OnConnectedAsync(HubCallerContext context, IGroupManager groups)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(groups);
            return OnConnectedCoreAsync(context, groups);
        }

        private async Task OnConnectedCoreAsync(HubCallerContext context, IGroupManager groups)
        {
            var caller = EnterContext(context);
            await _notificationService.CreateConnectionAsync(context.ConnectionId);

            var organizationIds = await OrganizationIdsAsync(caller);
            foreach (var group in NotificationHubGroups.For(caller, organizationIds))
            {
                await groups.AddToGroupAsync(context.ConnectionId, group, context.ConnectionAborted);
            }
        }

        public Task OnDisconnectedAsync(HubCallerContext context, Exception? exception)
        {
            ArgumentNullException.ThrowIfNull(context);
            EnterContext(context);
            return _notificationService.RemoveCollectionAsync(context.ConnectionId);
        }

        public Task SubscribeAsync(HubCallerContext context, string command)
        {
            ArgumentNullException.ThrowIfNull(context);
            var caller = EnterContext(context);
            return _notificationService.AddSubscriptionAsync(BuildSubscription(context, caller, command));
        }

        public Task UnsubscribeAsync(HubCallerContext context, string command)
        {
            ArgumentNullException.ThrowIfNull(context);
            var caller = EnterContext(context);
            return _notificationService.RemoveSubscriptionAsync(BuildSubscription(context, caller, command));
        }

        /// <summary>
        /// A failed lookup must not refuse the connection: the user still gets the tenant and user groups, only
        /// organization-wide live sends are missed (they stay in the inbox).
        /// </summary>
        private async Task<IReadOnlyList<string>> OrganizationIdsAsync(HubCaller caller)
        {
            if (string.IsNullOrWhiteSpace(caller.UserId))
                return [];

            try
            {
                return await _users.GetOrganizationIdsAsync(caller.UserId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Type only: a driver message can carry the user id.
                _logger.LogWarning("Hub connection: organizations not read ({Error}); no organization groups joined", // NOSONAR S6667 — type only
                    ex.GetType().Name);
                return [];
            }
        }

        /// <summary>
        /// The subscription for this connection. The user is always the caller's own (token <c>user_id</c>); user ids
        /// sent by the client are ignored, and logged when they differ.
        /// </summary>
        private Subscription BuildSubscription(HubCallerContext context, HubCaller caller, string command)
        {
            var message = JsonSerializer.Deserialize<NotifierPayload>(command)
                ?? throw new ArgumentException("The subscription command is empty.", nameof(command));

            if (message.UserIds?.Exists(id => id != caller.UserId) == true)
            {
                _logger.LogWarning("Hub subscription for connection {ConnectionId} named other user ids; the caller's own user id is used",
                    context.ConnectionId);
            }

            return new Subscription
            {
                Payload =
                {
                    ConnectionId = context.ConnectionId,
                    SubscriptionFilters = message.SubscriptionFilters,
                    UserIds = string.IsNullOrEmpty(caller.UserId) ? [] : [caller.UserId],
                }
            };
        }

        /// <summary>The hub runs outside the HTTP pipeline: set the context the repositories read.</summary>
        private static HubCaller EnterContext(HubCallerContext context)
        {
            Thread.CurrentPrincipal = context.User;
            var caller = HubCaller.From(context.User);
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: caller.TenantId,
                roles: [],
                userId: caller.UserId,
                userName: string.Empty,
                isAuthenticated: false,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.MinValue,
                email: string.Empty,
                permissions: [],
                phoneNumber: string.Empty,
                displayName: string.Empty,
                oauthToken: string.Empty,
                originalTenantId: caller.OriginalTenantId,
                impersonated: caller.Impersonated));
            return caller;
        }
    }
}
