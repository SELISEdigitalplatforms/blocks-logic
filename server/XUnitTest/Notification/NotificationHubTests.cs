using System.Diagnostics;
using System.Security.Claims;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="NotificationHub"/>. The hub caller context is faked, so the
    /// tests assert which connection the hub registers, what it forwards to the notification
    /// service for a subscribe or unsubscribe command, and how it reacts to failures.
    /// </summary>
    public class NotificationHubTests : IDisposable
    {
        private const string ConnectionId = "conn-1";

        private readonly Mock<INotificationService> _service = new();
        private readonly Mock<HubCallerContext> _callerContext = new();
        private readonly NotificationHub _sut;

        public NotificationHubTests()
        {
            _callerContext.SetupGet(c => c.ConnectionId).Returns(ConnectionId);
            _callerContext.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));

            _sut = new NotificationHub(_service.Object, Mock.Of<ILogger<NotificationHub>>())
            {
                Context = _callerContext.Object,
            };
        }

        public void Dispose()
        {
            _sut.Dispose();
            BlocksContext.ClearContext();
            GC.SuppressFinalize(this);
        }

        private static string Command(params (string Context, string Action, string Value)[] filters) =>
            System.Text.Json.JsonSerializer.Serialize(new NotifierPayload
            {
                UserIds = ["user-1"],
                SubscriptionFilters = [.. filters.Select(f => new SubscriptionFilter
                {
                    Context = f.Context,
                    ActionName = f.Action,
                    Value = f.Value,
                })],
            });

        [Fact]
        public async Task OnConnectedAsync_RegistersTheConnection()
        {
            await _sut.OnConnectedAsync();

            _service.Verify(s => s.CreateConnectionAsync(ConnectionId), Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_RethrowsWhenTheConnectionCannotBeRegistered()
        {
            _service.Setup(s => s.CreateConnectionAsync(It.IsAny<string>()))
                    .ThrowsAsync(new InvalidOperationException("mongo down"));

            var act = () => _sut.OnConnectedAsync();

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("mongo down");
        }

        [Fact]
        public async Task OnDisconnectedAsync_RemovesTheConnectionAndItsSubscriptions()
        {
            await _sut.OnDisconnectedAsync(null);

            _service.Verify(s => s.RemoveCollectionAsync(ConnectionId), Times.Once);
        }

        [Fact]
        public async Task OnDisconnectedAsync_RemovesTheConnectionEvenWhenTheClientDroppedWithAnError()
        {
            await _sut.OnDisconnectedAsync(new IOException("client vanished"));

            _service.Verify(s => s.RemoveCollectionAsync(ConnectionId), Times.Once);
        }

        [Fact]
        public async Task OnDisconnectedAsync_RethrowsWhenTheCleanupFails()
        {
            _service.Setup(s => s.RemoveCollectionAsync(It.IsAny<string>()))
                    .ThrowsAsync(new InvalidOperationException("mongo down"));

            var act = () => _sut.OnDisconnectedAsync(null);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Subscribe_ForwardsTheFiltersOfTheCommandForTheCallingConnection()
        {
            Subscription? captured = null;
            _service.Setup(s => s.AddSubscriptionAsync(It.IsAny<Subscription>()))
                    .Callback<Subscription>(s => captured = s)
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await _sut.Subscribe(Command(("orders", "created", "1"), ("orders", "updated", "2")));

            captured.Should().NotBeNull();
            captured!.Payload.ConnectionId.Should().Be(ConnectionId);
            captured.Payload.UserIds.Should().Equal("user-1");
            captured.Payload.SubscriptionFilters.Should().HaveCount(2);
            captured.Payload.SubscriptionFilters!.Should()
                    .Contain(f => f.Context == "orders" && f.ActionName == "created" && f.Value == "1");
        }

        [Fact]
        public async Task Subscribe_RethrowsAndSubscribesNothingWhenTheCommandIsNotJson()
        {
            var act = () => _sut.Subscribe("not json");

            await act.Should().ThrowAsync<Exception>();
            _service.Verify(s => s.AddSubscriptionAsync(It.IsAny<Subscription>()), Times.Never);
        }

        [Fact]
        public async Task Subscribe_RethrowsWhenTheCommandIsAJsonNull()
        {
            // Documents current behaviour: a null command deserialises to a null payload and
            // is dereferenced without a guard.
            var act = () => _sut.Subscribe("null");

            await act.Should().ThrowAsync<NullReferenceException>();
            _service.Verify(s => s.AddSubscriptionAsync(It.IsAny<Subscription>()), Times.Never);
        }

        [Fact]
        public async Task Subscribe_RethrowsWhenTheSubscriptionCannotBeStored()
        {
            _service.Setup(s => s.AddSubscriptionAsync(It.IsAny<Subscription>()))
                    .ThrowsAsync(new InvalidOperationException("mongo down"));

            var act = () => _sut.Subscribe(Command(("orders", "created", "1")));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Unsubscribe_ForwardsTheFiltersOfTheCommandForTheCallingConnection()
        {
            Subscription? captured = null;
            _service.Setup(s => s.RemoveSubscriptionAsync(It.IsAny<Subscription>()))
                    .Callback<Subscription>(s => captured = s)
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });

            await _sut.Unsubscribe(Command(("orders", "created", "1")));

            captured.Should().NotBeNull();
            captured!.Payload.ConnectionId.Should().Be(ConnectionId);
            captured.Payload.UserIds.Should().Equal("user-1");
            captured.Payload.SubscriptionFilters.Should().ContainSingle();
        }

        [Fact]
        public async Task Unsubscribe_RethrowsAndUnsubscribesNothingWhenTheCommandIsNotJson()
        {
            var act = () => _sut.Unsubscribe("not json");

            await act.Should().ThrowAsync<Exception>();
            _service.Verify(s => s.RemoveSubscriptionAsync(It.IsAny<Subscription>()), Times.Never);
        }

        [Fact]
        public async Task Unsubscribe_RethrowsWhenTheSubscriptionCannotBeRemoved()
        {
            _service.Setup(s => s.RemoveSubscriptionAsync(It.IsAny<Subscription>()))
                    .ThrowsAsync(new InvalidOperationException("mongo down"));

            var act = () => _sut.Unsubscribe(Command(("orders", "created", "1")));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // The hub installs the ambient context before it calls the service, and that context
        // lives in async local storage, so the tests below read it from inside that call.

        [Fact]
        public async Task OnConnectedAsync_TakesTheTenantAndUserFromTheCallerClaimsWhileTracing()
        {
            _callerContext.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", "tenant-x"),
                new Claim("user_id", "user-x"),
                new Claim("original_tenant_id", "tenant-x"),
            ])));

            BlocksContext? seen = null;
            _service.Setup(s => s.CreateConnectionAsync(It.IsAny<string>()))
                    .Callback(() => seen = BlocksContext.GetContext())
                    .Returns(Task.CompletedTask);

            using var activity = new Activity("hub-test").Start();

            await _sut.OnConnectedAsync();

            seen.Should().NotBeNull();
            seen!.TenantId.Should().Be("tenant-x");
            seen.UserId.Should().Be("user-x");
            seen.OriginalTenantId.Should().Be("tenant-x");
        }

        [Fact]
        public async Task OnConnectedAsync_FallsBackToAnEmptyTenantWhenTheCallerHasNoClaims()
        {
            BlocksContext? seen = null;
            _service.Setup(s => s.CreateConnectionAsync(It.IsAny<string>()))
                    .Callback(() => seen = BlocksContext.GetContext())
                    .Returns(Task.CompletedTask);

            using var activity = new Activity("hub-test").Start();

            await _sut.OnConnectedAsync();

            seen.Should().NotBeNull();
            seen!.TenantId.Should().BeEmpty();
            seen.UserId.Should().BeEmpty();
        }

        [Fact]
        public async Task OnConnectedAsync_LeavesTheAmbientContextAloneWhenNothingIsTracing()
        {
            BlocksContext? seen = null;
            _service.Setup(s => s.CreateConnectionAsync(It.IsAny<string>()))
                    .Callback(() => seen = BlocksContext.GetContext())
                    .Returns(Task.CompletedTask);

            Activity.Current = null;

            await _sut.OnConnectedAsync();

            seen.Should().BeNull("without an activity the hub never installs a context");
            _service.Verify(s => s.CreateConnectionAsync(ConnectionId), Times.Once);
        }
    }
}
