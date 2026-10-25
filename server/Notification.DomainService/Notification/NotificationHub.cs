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
        private const string OriginalTenantIdClaim = "original_tenant_id";
        private const string ImpersonatedClaim = "impersonated";

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
                // Broadcasts go to these groups only (BroadcastReceiver), so they never cross tenants.
                foreach (var groupTenantId in BroadcastTenantIds())
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.ForTenant(groupTenantId));
                }
                if (ImpersonatedUserGroup() is { } userGroup)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, userGroup);
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

        /// <summary>
        /// The tenants whose broadcasts reach this connection: the token tenant, and for an impersonated
        /// root tenant user also the original (root) tenant, because the Blocks services broadcast with the root key.
        /// </summary>
        private List<string> BroadcastTenantIds()
        {
            var tenantIds = new List<string>();
            var tenantId = Context.User?.FindFirst(TenantIdClaim)?.Value;
            if (!string.IsNullOrWhiteSpace(tenantId))
                tenantIds.Add(tenantId);

            var originalTenantId = Context.User?.FindFirst(OriginalTenantIdClaim)?.Value;
            if (IsImpersonated() && !string.IsNullOrWhiteSpace(originalTenantId) && !tenantIds.Contains(originalTenantId))
                tenantIds.Add(originalTenantId);

            return tenantIds;
        }

        /// <summary>
        /// For an impersonated root tenant user, the group of that user in the project (UserSpecificReceiver).
        /// </summary>
        private string? ImpersonatedUserGroup()
        {
            var tenantId = Context.User?.FindFirst(TenantIdClaim)?.Value;
            var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
            return IsImpersonated() && !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(userId)
                ? NotificationGroups.ForTenantUser(tenantId, userId)
                : null;
        }

        // Compared the same way Genesis reads the claim when it builds a context from an HTTP
        // request, so an impersonated connection and an impersonated request agree on which
        // database the repository resolves.
        private bool IsImpersonated() => Context.User?.FindFirst(ImpersonatedClaim)?.Value == "true";

        private void SetupActivity()
        {
            if (Activity.Current == null) return;

            var tenantId = Context.User?.FindFirst(TenantIdClaim)?.Value;
            var originaltenantId = Context.User?.FindFirst(OriginalTenantIdClaim)?.Value;
            var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
            var impersonated = IsImpersonated();

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
