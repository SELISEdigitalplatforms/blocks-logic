using System.Security.Claims;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// <see cref="NotificationHubHandler"/>: which groups a connection joins (genesis senders reach users only through
    /// groups), and that subscriptions are always the caller's own.
    /// </summary>
    public sealed class NotificationHubHandlerTests : IDisposable
    {
        private const string ConnectionId = "conn-1";

        private readonly Mock<INotificationService> _service = new();
        private readonly Mock<INotificationUserRepository> _users = new();
        private readonly Mock<HubCallerContext> _context = new();
        private readonly List<string> _joined = [];
        private readonly Mock<IGroupManager> _groups = new();
        private readonly CapturingLogger<NotificationHubHandler> _logger = new();
        private readonly NotificationHubHandler _sut;

        public NotificationHubHandlerTests()
        {
            BlocksContext.IsTestMode = true;
            _context.SetupGet(c => c.ConnectionId).Returns(ConnectionId);
            _groups.Setup(g => g.AddToGroupAsync(ConnectionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string, CancellationToken>((_, group, _) => _joined.Add(group))
                   .Returns(Task.CompletedTask);
            _users.Setup(u => u.GetOrganizationIdsAsync(It.IsAny<string>())).ReturnsAsync(["org-1", "org-2"]);
            _sut = new NotificationHubHandler(_service.Object, _users.Object, _logger);
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
            BlocksContext.IsTestMode = false;
        }

        private void Caller(string? userId, string? tenantId, string? originalTenantId = null, bool impersonated = false)
        {
            var claims = new List<Claim>();
            if (userId is not null) claims.Add(new Claim("user_id", userId));
            if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId));
            if (originalTenantId is not null) claims.Add(new Claim("original_tenant_id", originalTenantId));
            if (impersonated) claims.Add(new Claim("impersonated", "true"));
            _context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims)));
        }

        [Fact]
        public async Task Connect_JoinsTheTenantTheUserAndEachOrganizationOfTheUser()
        {
            Caller("user-1", "tenant-a");

            await _sut.OnConnectedAsync(_context.Object, _groups.Object);

            _service.Verify(s => s.CreateConnectionAsync(ConnectionId), Times.Once);
            _joined.Should().BeEquivalentTo(
                "tenant:tenant-a", "tenant:tenant-a:user:user-1", "tenant:tenant-a:org:org-1", "tenant:tenant-a:org:org-2");
        }

        [Fact]
        public async Task Connect_ImpersonatedRootUser_JoinsProjectAndRootGroups_AndOrganizationsOfRoot()
        {
            Caller("root-user", "project-p", originalTenantId: "root", impersonated: true);
            bool? readAsImpersonated = null;
            _users.Setup(u => u.GetOrganizationIdsAsync("root-user"))
                  .Callback(() => readAsImpersonated = BlocksContext.GetContext()?.Impersonated)
                  .ReturnsAsync(["root-org"]);

            await _sut.OnConnectedAsync(_context.Object, _groups.Object);

            readAsImpersonated.Should().BeTrue("the user and its organizations are read in the ROOT DB");
            _joined.Should().BeEquivalentTo(
                "tenant:project-p", "tenant:project-p:user:root-user",
                "tenant:root", "tenant:root:user:root-user",
                "tenant:root:org:root-org");
        }

        [Fact]
        public async Task Connect_NotImpersonated_IgnoresTheOriginalTenant()
        {
            Caller("user-1", "tenant-a", originalTenantId: "root");
            _users.Setup(u => u.GetOrganizationIdsAsync("user-1")).ReturnsAsync([]);

            await _sut.OnConnectedAsync(_context.Object, _groups.Object);

            _joined.Should().BeEquivalentTo("tenant:tenant-a", "tenant:tenant-a:user:user-1");
        }

        [Fact]
        public async Task Connect_OrganizationLookupFails_StillJoinsTenantAndUserGroups()
        {
            Caller("user-1", "tenant-a");
            _users.Setup(u => u.GetOrganizationIdsAsync("user-1")).ThrowsAsync(new TimeoutException("db down user-1"));

            await _sut.OnConnectedAsync(_context.Object, _groups.Object);

            _joined.Should().BeEquivalentTo("tenant:tenant-a", "tenant:tenant-a:user:user-1");
            _logger.Lines.Should().NotContain(m => m.Contains("user-1"), "logs never carry user ids");
        }

        [Fact]
        public async Task Connect_WithoutAUserClaim_JoinsOnlyTheTenantGroup()
        {
            Caller(null, "tenant-a");

            await _sut.OnConnectedAsync(_context.Object, _groups.Object);

            _joined.Should().Equal("tenant:tenant-a");
            _users.Verify(u => u.GetOrganizationIdsAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Subscribe_StoresTheCallersOwnUser_NotTheIdsTheClientSent()
        {
            Caller("user-1", "tenant-a");
            Subscription? stored = null;
            _service.Setup(s => s.AddSubscriptionAsync(It.IsAny<Subscription>()))
                    .Callback<Subscription>(s => stored = s)
                    .ReturnsAsync(new BaseResponse { IsSuccess = true });
            var command = JsonSerializer.Serialize(new NotifierPayload
            {
                UserIds = ["someone-else"],
                SubscriptionFilters = [new SubscriptionFilter { Context = "orders", ActionName = "created", Value = "1" }],
            });

            await _sut.SubscribeAsync(_context.Object, command);

            stored!.Payload.UserIds.Should().Equal("user-1");
            stored.Payload.ConnectionId.Should().Be(ConnectionId);
            _logger.Lines.Should().Contain(m => m.Contains("named other user ids"));
        }

        [Fact]
        public async Task Subscribe_ACommandThatIsNotJson_IsRefusedAndStoresNothing()
        {
            Caller("user-1", "tenant-a");

            var act = () => _sut.SubscribeAsync(_context.Object, "not json");

            await act.Should().ThrowAsync<JsonException>();
            _service.Verify(s => s.AddSubscriptionAsync(It.IsAny<Subscription>()), Times.Never);
        }

        [Fact]
        public async Task Disconnect_RemovesTheConnectionAndItsSubscriptions()
        {
            Caller("user-1", "tenant-a");

            await _sut.OnDisconnectedAsync(_context.Object, new IOException("dropped"));

            _service.Verify(s => s.RemoveCollectionAsync(ConnectionId), Times.Once);
        }
    }
}
