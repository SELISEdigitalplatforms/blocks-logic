using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Logic;

namespace DomainService.Notification
{
    public class UserSpecificReceiver : IStrategicClientProvider
    {
        private const string _usersCollection = "Users";

        private readonly ILogger<UserSpecificReceiver> _logger;
        private readonly INotificationRepository _repository;
        private readonly IHubContext<NotificationHub> _hubContext;

        public UserSpecificReceiver(INotificationRepository repository,
                                    ILogger<UserSpecificReceiver> logger,
                                    IHubContext<NotificationHub> hubContext)
        {
            _repository = repository;
            _logger = logger;
            _hubContext = hubContext;
        }
        public async Task<IClientProxy> GetClientAsync(NotifierPayload notifierPayload)
        {
            _logger.LogInformation("UserSpecificReceiver: GetClientAsync called for connection {ConnectionId}", notifierPayload.ConnectionId);
            var hasUserIds = notifierPayload.UserIds != null && notifierPayload.UserIds.Count != 0;
            var hasRoles = notifierPayload.Roles != null && notifierPayload.Roles.Count != 0;
            var hasOrganizationIds = notifierPayload.OrganizationIds != null && notifierPayload.OrganizationIds.Count != 0;
            _logger.LogInformation("UserSpecificReceiver: hasUserIds={hasUserIds}, hasRoles={hasRoles}, hasOrganizationIds={hasOrganizationIds}", hasUserIds, hasRoles, hasOrganizationIds);
            if (hasRoles)
            {
                var roleUserIds = await GetUserIdsByRolesAsync(notifierPayload.Roles, notifierPayload.OrganizationIds);
                if (roleUserIds.Count != 0)
                {
                    notifierPayload.UserIds = roleUserIds;
                }
            }
            else if (!hasUserIds && hasOrganizationIds)
            {
                var organizationUserIds = await GetUserIdsByOrganizationsAsync(notifierPayload.OrganizationIds);
                if (organizationUserIds.Count != 0)
                {
                    notifierPayload.UserIds = organizationUserIds;
                }
            }

            var connectionIdStrings = (await _repository.GetItemsAsync<NotificationConnection>(p => notifierPayload.UserIds.Contains(p.UserId))).ToList();
            var connections = _hubContext.Clients.Clients(connectionIdStrings.Select(p => p.ConnectionId).ToList());

            var userGroups = ImpersonatedUserGroups(notifierPayload.UserIds);
            if (userGroups.Count != 0)
            {
                // Root tenant users working in this project keep their connection row in the root DB, so a
                // sender using this project's key reaches them through their project user group.
                return new CompositeClientProxy(connections, _hubContext.Clients.Groups(userGroups));
            }

            if (connectionIdStrings.Count == 0)
            {
                // The send still succeeds; without this line a notification that reaches no one is invisible.
                _logger.LogWarning("UserSpecificReceiver: no open connection for the {UserCount} target user(s) in tenant {TenantId}; nothing delivered live",
                    notifierPayload.UserIds?.Count ?? 0, BlocksContext.GetContext()?.TenantId);
            }

            return connections;
        }

        /// <summary>
        /// The project user groups of the target users, for a send with a project's own key. An impersonated
        /// send already reads the root DB and finds those connections, so it gets none (no double delivery).
        /// </summary>
        private static List<string> ImpersonatedUserGroups(List<string>? userIds)
        {
            var context = BlocksContext.GetContext();
            if (context is null || context.Impersonated || string.IsNullOrWhiteSpace(context.TenantId) || userIds is null)
                return [];

            return userIds.Where(id => !string.IsNullOrWhiteSpace(id))
                          .Distinct()
                          .Select(id => NotificationGroups.ForTenantUser(context.TenantId, id))
                          .ToList();
        }

        private async Task<List<string>> GetUserIdsByRolesAsync(List<string> roles, List<string> organizationIds)
        {
            var hasOrganizationFilter = organizationIds != null && organizationIds.Count != 0;
            var users = await GetUsersByOrganizationsAsync(organizationIds);

            return users
                .Where(u => u.Roles != null && u.Roles.Any(orgRoles =>
                    (!hasOrganizationFilter || organizationIds.Contains(orgRoles.Key)) &&
                    orgRoles.Value != null && orgRoles.Value.Any(role => roles.Contains(role))))
                .Select(u => u.ItemId)
                .Distinct()
                .ToList();
        }

        private async Task<List<string>> GetUserIdsByOrganizationsAsync(List<string> organizationIds)
        {
            var users = await GetUsersByOrganizationsAsync(organizationIds);
            return users.Select(u => u.ItemId).Distinct().ToList();
        }
        private async Task<List<NotificationUser>> GetUsersByOrganizationsAsync(List<string> organizationIds)
        {
            var hasOrganizationFilter = organizationIds != null && organizationIds.Count != 0;

            return await _repository.GetItemsAsync<NotificationUser>(
                u => !hasOrganizationFilter || u.OrganizationIds.Any(organizationId => organizationIds.Contains(organizationId)),
                _usersCollection);
        }
    }
}
