using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

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
            _logger.LogInformation("UserSpecificReceiver: GetClientAsync called with notifierPayload: {@notifierPayload}", notifierPayload);
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

            return _hubContext.Clients.Clients(connectionIdStrings.Select(p => p.ConnectionId).ToList());
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
