using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.AspNetCore.SignalR;

namespace DomainService.Notification
{
    /// <summary>
    /// A broadcast reaches every connection of the sender's tenant (the request tenant), never other
    /// tenants. With no tenant on the request it reaches no one.
    /// </summary>
    public class BroadcastReceiver : IStrategicClientProvider
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public BroadcastReceiver(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task<IClientProxy> GetClientAsync(NotifierPayload notifierPayload)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            var client = string.IsNullOrWhiteSpace(tenantId)
                ? _hubContext.Clients.Clients(Array.Empty<string>())
                : _hubContext.Clients.Group(NotificationGroups.ForTenant(tenantId));
            return Task.FromResult(client);
        }
    }
}
