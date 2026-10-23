using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;


namespace DomainService.Notification
{
    [Authorize]
    public class NotificationHub : Hub
    {
        private const string TenantIdClaim = "tenant_id";
        private const string UserIdClaim = "user_id";

        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(INotificationService notificationService,
                                ILogger<NotificationHub> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        private void SetPrincipal()
        {
            Thread.CurrentPrincipal = Context.User;
            SetupActivity();
        }

        public async override Task OnConnectedAsync()
        {
            try
            {
                SetPrincipal();
                await _notificationService.CreateConnectionAsync(Context.ConnectionId);
                var tenantId = Context.User?.FindFirst(TenantIdClaim)?.Value;
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    // Broadcasts go to this group only (BroadcastReceiver), so they never cross tenants.
                    await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.ForTenant(tenantId));
                }
                await  base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        public async override Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                SetPrincipal();
                await _notificationService.RemoveCollectionAsync(Context.ConnectionId);
                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        public async Task Subscribe(string command)
        {
            try
            {
                SetPrincipal();
                var request = BuildSubscription(command);
                await _notificationService.AddSubscriptionAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        public async Task Unsubscribe(string command)
        {
            try
            {
                SetPrincipal();
                var request = BuildSubscription(command);
                await _notificationService.RemoveSubscriptionAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// The subscription for this connection. The user is always the caller's own (token
        /// <c>user_id</c>); user ids sent by the client are ignored, and logged when they differ.
        /// </summary>
        private Subscription BuildSubscription(string command)
        {
            var message = JsonSerializer.Deserialize<NotifierPayload>(command)
                ?? throw new ArgumentException("The subscription command is empty.", nameof(command));
            var userId = Context.User?.FindFirst(UserIdClaim)?.Value ?? string.Empty;

            if (message.UserIds?.Exists(id => id != userId) == true)
            {
                _logger.LogWarning("Hub subscription for connection {ConnectionId} named other user ids; the caller's own user id is used",
                    Context.ConnectionId);
            }

            return new Subscription
            {
                Payload =
                {
                    ConnectionId = Context.ConnectionId,
                    SubscriptionFilters = message.SubscriptionFilters,
                    UserIds = string.IsNullOrEmpty(userId) ? [] : [userId],
                }
            };
        }

        private void SetupActivity()
        {
            if (Activity.Current == null) return;

            var tenantId = Context.User?.FindFirst(TenantIdClaim)?.Value;
            var originaltenantId = Context.User?.FindFirst("original_tenant_id")?.Value;
            var userId = Context.User?.FindFirst(UserIdClaim)?.Value;

            // Compared the same way Genesis reads the claim when it builds a context from an HTTP
            // request, so an impersonated connection and an impersonated request agree on which
            // database the repository resolves.
            var impersonated = Context.User?.FindFirst("impersonated")?.Value == "true";

            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId ?? string.Empty,
                roles: Array.Empty<string>(),
                userId: userId ?? string.Empty,
                userName: string.Empty,
                isAuthenticated: false,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.MinValue,
                email: string.Empty,
                permissions: Array.Empty<string>(),
                phoneNumber: string.Empty,
                displayName: string.Empty,
                oauthToken: string.Empty,
                //refreshToken: string.Empty,
                originalTenantId: originaltenantId,
                impersonated: impersonated
            ));

        }
    }
}
