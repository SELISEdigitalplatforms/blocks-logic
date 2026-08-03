using DomainService.Notification;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace XUnitTest.Notification
{
    // SignalR doubles shared by the receiver and notifier tests. The client lookups
    // return recording proxies, so a test can assert both which connections were
    // addressed and what was pushed to them without a running hub.
    internal sealed class HubContextDouble
    {
        public HubContextDouble()
        {
            Clients.Setup(c => c.All).Returns(AllProxy.Object);
            Clients.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>()))
                   .Callback<IReadOnlyList<string>>(ids => AddressedConnectionIds = ids)
                   .Returns(SelectedProxy.Object);
            HubContext.Setup(h => h.Clients).Returns(Clients.Object);
        }

        public Mock<IHubContext<NotificationHub>> HubContext { get; } = new();

        public Mock<IHubClients> Clients { get; } = new();

        public Mock<IClientProxy> AllProxy { get; } = new();

        public Mock<IClientProxy> SelectedProxy { get; } = new();

        // The connection ids handed to IHubClients.Clients on the last call.
        public IReadOnlyList<string>? AddressedConnectionIds { get; private set; }

        public IHubContext<NotificationHub> Object => HubContext.Object;
    }
}
