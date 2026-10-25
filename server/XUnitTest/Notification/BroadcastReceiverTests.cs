using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Blocks.Genesis.Entities.Logic;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="BroadcastReceiver"/>. A broadcast ignores the payload and reaches only the
    /// connection group of the request tenant; with no tenant it reaches no one, and never every client.
    /// </summary>
    public class BroadcastReceiverTests : IDisposable
    {
        private readonly HubContextDouble _hub = new();
        private readonly BroadcastReceiver _sut;

        public BroadcastReceiverTests()
        {
            _sut = new BroadcastReceiver(_hub.Object);
            TestBlocksContext.Set("tenant-a");
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task GetClientAsync_ReturnsTheConnectionGroupOfTheRequestTenant()
        {
            var client = await _sut.GetClientAsync(new NotifierPayload());

            client.Should().BeSameAs(_hub.GroupProxy.Object);
            _hub.AddressedGroup.Should().Be("tenant:tenant-a");
            _hub.Clients.Verify(c => c.Clients(It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task GetClientAsync_NeverReachesEveryConnectedClient()
        {
            await _sut.GetClientAsync(new NotifierPayload());

            _hub.Clients.Verify(c => c.All, Times.Never);
        }

        [Fact]
        public async Task GetClientAsync_ReachesNoOneWhenTheRequestHasNoTenant()
        {
            TestBlocksContext.Clear();

            var client = await _sut.GetClientAsync(new NotifierPayload());

            client.Should().BeSameAs(_hub.SelectedProxy.Object);
            _hub.AddressedConnectionIds.Should().BeEmpty();
            _hub.Clients.Verify(c => c.All, Times.Never);
            _hub.Clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetClientAsync_ReachesNoOneWhenTheTenantIdIsEmpty()
        {
            TestBlocksContext.Set("");

            var client = await _sut.GetClientAsync(new NotifierPayload());

            client.Should().BeSameAs(_hub.SelectedProxy.Object);
            _hub.AddressedConnectionIds.Should().BeEmpty();
            _hub.Clients.Verify(c => c.All, Times.Never);
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
            client.Should().BeSameAs(_hub.GroupProxy.Object);
            payload.UserIds.Should().Equal("user-1");
            payload.SubscriptionFilters.Should().ContainSingle();
        }

        [Fact]
        public async Task GetClientAsync_ToleratesANullPayload()
        {
            var client = await _sut.GetClientAsync(null!);

            client.Should().BeSameAs(_hub.GroupProxy.Object);
        }
    }
}
