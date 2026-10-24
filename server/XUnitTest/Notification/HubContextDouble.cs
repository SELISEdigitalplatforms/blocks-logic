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
            Clients.Setup(c => c.Group(It.IsAny<string>()))
                   .Callback<string>(name => AddressedGroup = name)
                   .Returns(GroupProxy.Object);
            Clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
                   .Callback<IReadOnlyList<string>>(names => AddressedGroups = names)
                   .Returns(GroupsProxy.Object);
            HubContext.Setup(h => h.Clients).Returns(Clients.Object);
        }

        public Mock<IHubContext<NotificationHub>> HubContext { get; } = new();

        public Mock<IHubClients> Clients { get; } = new();

        public Mock<IClientProxy> AllProxy { get; } = new();

        public Mock<IClientProxy> SelectedProxy { get; } = new();

        public Mock<IClientProxy> GroupProxy { get; } = new();

        public Mock<IClientProxy> GroupsProxy { get; } = new();

        // The group names handed to IHubClients.Groups on the last call.
        public IReadOnlyList<string>? AddressedGroups { get; private set; }

        // The group name handed to IHubClients.Group on the last call.
        public string? AddressedGroup { get; private set; }

        // The connection ids handed to IHubClients.Clients on the last call.
        public IReadOnlyList<string>? AddressedConnectionIds { get; private set; }

        public IHubContext<NotificationHub> Object => HubContext.Object;
    }
}
