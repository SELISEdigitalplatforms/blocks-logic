using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Audience rules and sender narrowing. Users, permission grants, subscriptions and connections are in memory; the
    /// fake applies the database narrowing the resolver asks for, so a wrong narrowing loses users here too.
    /// </summary>
    public class NotificationAudienceResolverTests
    {
        private readonly FakeAudienceRepository _audience = new();
        private readonly Mock<INotificationRepository> _notifications = new();
        private readonly List<NotificationSubscription> _subscriptions = [];
        private readonly List<NotificationConnection> _connections = [];

        public NotificationAudienceResolverTests()
        {
            _notifications.Setup(r => r.GetItemsAsync(It.IsAny<Expression<Func<NotificationSubscription, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((Expression<Func<NotificationSubscription, bool>> f, string _) => _subscriptions.Where(f.Compile()).ToList());
            _notifications.Setup(r => r.GetItemsAsync(It.IsAny<Expression<Func<NotificationConnection, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((Expression<Func<NotificationConnection, bool>> f, string _) => _connections.Where(f.Compile()).ToList());

            // org-a: alice (admin), bob (viewer). org-b: carol (admin), dave (viewer, direct "reports.read").
            _audience.Users.Add(User("alice", ("org-a", ["admin"], [])));
            _audience.Users.Add(User("bob", ("org-a", ["viewer"], [])));
            _audience.Users.Add(User("carol", ("org-b", ["admin"], [])));
            _audience.Users.Add(User("dave", ("org-b", ["viewer"], ["reports.read"])));
        }

        private NotificationAudienceResolver Sut() => new(_audience, _notifications.Object);

        private static AudienceUser User(string id, params (string Org, List<string> Roles, List<string> Permissions)[] orgs) =>
            new(id, orgs.Select(o => o.Org).ToList(),
                orgs.ToDictionary(o => o.Org, o => o.Roles),
                orgs.ToDictionary(o => o.Org, o => o.Permissions));

        private async Task<IReadOnlyList<string>> Resolve(NotificationAudience audience, NotifierPayload? sender = null) =>
            (await Sut().ResolveAsync(audience, sender ?? new NotifierPayload())).UserIds;

        [Fact]
        public async Task EveryoneInTenant_IsEveryActiveUser()
        {
            (await Resolve(new NotificationAudience { EveryoneInTenant = true }))
                .Should().BeEquivalentTo("alice", "bob", "carol", "dave");
        }

        [Fact]
        public async Task MatchAny_IsTheUnionOfTheLists()
        {
            var audience = new NotificationAudience { UserIds = ["bob"], Roles = ["admin"] };

            (await Resolve(audience)).Should().BeEquivalentTo("alice", "bob", "carol");
        }

        [Fact]
        public async Task MatchAll_IsTheIntersectionOfTheLists()
        {
            var audience = new NotificationAudience { Match = NotificationAudienceMatch.All, UserIds = ["alice", "bob", "carol"], Roles = ["admin"] };

            (await Resolve(audience)).Should().BeEquivalentTo("alice", "carol");
        }

        [Fact]
        public async Task UserIds_UnknownOrInactiveIds_AreLeftOut()
        {
            (await Resolve(new NotificationAudience { UserIds = ["alice", "ghost"] })).Should().Equal("alice");
        }

        [Fact]
        public async Task Roles_WithOrganizations_CountOnlyRolesHeldInsideThoseOrganizations()
        {
            // carol is admin, but in org-b; org-a members without the role are not added either (Match All).
            var audience = new NotificationAudience { Match = NotificationAudienceMatch.All, OrganizationIds = ["org-a"], Roles = ["admin"] };

            (await Resolve(audience)).Should().Equal("alice");
        }

        [Fact]
        public async Task Roles_WithOrganizations_MatchAny_OrganizationIsAlsoItsOwnSet()
        {
            var audience = new NotificationAudience { OrganizationIds = ["org-a"], Roles = ["admin"] };

            (await Resolve(audience)).Should().BeEquivalentTo("alice", "bob");
        }

        [Fact]
        public async Task Permission_ComesFromARoleGrantOfThatOrganization_OrADirectGrant()
        {
            _audience.Grants.Add(new PermissionRoleGrant("org-a", ["admin"]));

            (await Resolve(new NotificationAudience { Permissions = ["reports.read"] }))
                .Should().BeEquivalentTo(["alice", "dave"], "alice via the org-a admin grant, dave directly; carol's admin is in org-b");
        }

        [Fact]
        public async Task Permission_WithOrganizations_IgnoresGrantsOutsideThem()
        {
            _audience.Grants.Add(new PermissionRoleGrant("org-a", ["admin"]));
            var audience = new NotificationAudience { Match = NotificationAudienceMatch.All, OrganizationIds = ["org-a"], Permissions = ["reports.read"] };

            (await Resolve(audience)).Should().Equal("alice");
        }

        [Fact]
        public async Task Filters_AreUsersWithALiveSubscribedConnection()
        {
            _subscriptions.Add(new NotificationSubscription { Context = "orders", ActionName = "", Value = "", ConnectionId = "c-bob" });
            _subscriptions.Add(new NotificationSubscription { Context = "orders", ActionName = "", Value = "", ConnectionId = "c-gone" });
            _connections.Add(new NotificationConnection { ConnectionId = "c-bob", UserId = "bob" });

            var audience = new NotificationAudience { Filters = [new SubscriptionFilter { Context = "orders", ActionName = "created", Value = "7" }] };

            (await Resolve(audience)).Should().Equal("bob");
        }

        [Fact]
        public async Task Filters_NoSubscriber_IsAnEmptySet_NotNoRule()
        {
            var audience = new NotificationAudience { Filters = [new SubscriptionFilter { Context = "orders" }] };

            (await Resolve(audience)).Should().BeEmpty();
        }

        [Fact]
        public async Task Sender_CanOnlyNarrow_IdsOutsideTheAudienceAreDroppedAndCounted()
        {
            var audience = new NotificationAudience { OrganizationIds = ["org-a"] };

            var result = await Sut().ResolveAsync(audience, new NotifierPayload { UserIds = ["alice", "carol", "ghost"] });

            result.UserIds.Should().Equal("alice");
            result.AudienceCount.Should().Be(2);
            result.DroppedRequestedCount.Should().Be(2);
        }

        [Fact]
        public async Task Sender_Roles_NarrowTheAudience()
        {
            var audience = new NotificationAudience { EveryoneInTenant = true };

            (await Resolve(audience, new NotifierPayload { Roles = ["viewer"], OrganizationIds = ["org-b"] })).Should().Equal("dave");
        }

        [Fact]
        public async Task Sender_GaveNothing_TheAudienceIsTheAnswer()
        {
            var result = await Sut().ResolveAsync(new NotificationAudience { Roles = ["viewer"] }, new NotifierPayload());

            result.UserIds.Should().BeEquivalentTo("bob", "dave");
            result.DroppedRequestedCount.Should().Be(0);
        }

        private sealed class FakeAudienceRepository : INotificationAudienceRepository
        {
            public List<AudienceUser> Users { get; } = [];
            public List<PermissionRoleGrant> Grants { get; } = [];

            public async IAsyncEnumerable<AudienceUser> GetActiveUsersAsync(AudienceUserQuery query,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var user in Users.Where(u =>
                    (query.UserIds.Count == 0 || query.UserIds.Contains(u.ItemId))
                    && (query.OrganizationIds.Count == 0 || u.OrganizationIds.Any(query.OrganizationIds.Contains))))
                {
                    await Task.Yield();
                    yield return user;
                }
            }

            public Task<List<PermissionRoleGrant>> GetRoleGrantsAsync(IReadOnlyCollection<string> resources, CancellationToken cancellationToken = default) =>
                Task.FromResult(Grants.ToList());
        }
    }
}
