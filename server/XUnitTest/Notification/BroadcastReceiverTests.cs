using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="BroadcastReceiver"/>. A broadcast ignores the payload
    /// entirely and always resolves to every connected client.
    /// </summary>
    public class BroadcastReceiverTests
    {
        private readonly HubContextDouble _hub = new();
        private readonly BroadcastReceiver _sut;

        public BroadcastReceiverTests()
        {
            _sut = new BroadcastReceiver(_hub.Object);
        }

        [Fact]
        public async Task GetClientAsync_ReturnsEveryConnectedClient()
        {
            var client = await _sut.GetClientAsync(new NotifierPayload());

            client.Should().BeSameAs(_hub.AllProxy.Object);
            _hub.Clients.Verify(c => c.Clients(It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task GetClientAsync_IgnoresTheUserAndFilterSelectionOnThePayload()
        {
            var payload = new NotifierPayload
            {
                UserIds = ["user-1"],
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders" }],
            };

            var client = await _sut.GetClientAsync(payload);

            // A broadcast must not rewrite the selection carried by the payload.
            client.Should().BeSameAs(_hub.AllProxy.Object);
            payload.UserIds.Should().Equal("user-1");
            payload.SubscriptionFilters.Should().ContainSingle();
        }

        [Fact]
        public async Task GetClientAsync_ToleratesANullPayload()
        {
            var client = await _sut.GetClientAsync(null!);

            client.Should().BeSameAs(_hub.AllProxy.Object);
        }
    }
}
