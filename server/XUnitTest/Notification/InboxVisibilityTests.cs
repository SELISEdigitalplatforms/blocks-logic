using Blocks.Genesis.Entities.Logic;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using XUnitTest.Routing;
using XUnitTest.TestDoubles;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Inbox rows on local MongoDB, as genesis writes them: one row per user, or one row for a whole organization
    /// ("All users": no user id, OrganizationId set), and console users' rows in the ROOT DB with the project tenant id.
    /// </summary>
    public sealed class InboxVisibilityTests : IDisposable
    {
        private const string Collection = "OfflineNotifications";
        private const string Alice = "alice";
        private const string Bob = "bob";
        private const string Org1 = "org-1";

        private readonly TenantPlacementFixture _fixture = new();
        private readonly NotificationRepository _repository;

        public InboxVisibilityTests()
        {
            _repository = new NotificationRepository(_fixture.Provider, _fixture.Secret,
                NullLogger<NotificationRepository>.Instance, new PassThroughLogicDataAccess());
            TestBlocksContext.Set("dev", Alice);
        }

        public void Dispose() => _fixture.Dispose();

        private static InboxViewer Viewer(string userId, params string[] organizationIds) => new(userId, organizationIds, null);

        private static OfflineNotification Row(string id, string? userId = null, string? organizationId = null, string? tenantId = null) => new()
        {
            Id = id,
            CorrelationId = id,
            CreatedTime = DateTime.UtcNow,
            Payload = new PayloadData { UserId = userId, SubscriptionFilters = [], NotificationType = "UserSpecificReceiverType" },
            DenormalizedPayload = "{}",
            OrganizationId = organizationId,
            TenantId = tenantId,
        };

        private Task SeedAsync(IMongoDatabase database, params OfflineNotification[] rows) =>
            database.GetCollection<OfflineNotification>(Collection).InsertManyAsync(rows);

        private async Task<List<string>> IdsAsync(InboxViewer viewer, bool unreadOnly = false)
        {
            var page = await _repository.GetNotificationsAsync(
                new GetNotificationsRequest { Page = 0, PageSize = 50, IsUnreadOnly = unreadOnly }, viewer);
            return page.Notifications.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        [Fact]
        public async Task OrganizationRow_IsListedForMembersOfThatOrganizationOnly()
        {
            await SeedAsync(_fixture.Dev,
                Row("own", userId: Alice),
                Row("org-1-wide", organizationId: Org1),
                Row("org-2-wide", organizationId: "org-2"),
                Row("bobs", userId: Bob));

            (await IdsAsync(Viewer(Alice, Org1))).Should().Equal("org-1-wide", "own");
            (await IdsAsync(Viewer(Alice))).Should().Equal(["own"], "a user in no organization gets no organization row");
        }

        [Fact]
        public async Task OrganizationRow_ReadByOneUser_StaysUnreadForTheOthers()
        {
            await SeedAsync(_fixture.Dev, Row("org-1-wide", organizationId: Org1));

            await _repository.MarkAsReadAsync(Viewer(Alice, Org1), "org-1-wide");

            (await IdsAsync(Viewer(Alice, Org1), unreadOnly: true)).Should().BeEmpty();
            (await IdsAsync(Viewer(Bob, Org1), unreadOnly: true)).Should().Equal("org-1-wide");
        }

        [Fact]
        public async Task MarkAll_TouchesOnlyRowsTheViewerSees()
        {
            await SeedAsync(_fixture.Dev,
                Row("own", userId: Alice),
                Row("org-1-wide", organizationId: Org1),
                Row("org-2-wide", organizationId: "org-2"),
                Row("bobs", userId: Bob));

            await _repository.MarkAllAsReadAsync(Viewer(Alice, Org1));

            var readByAlice = await _fixture.Dev.GetCollection<OfflineNotification>(Collection)
                .Find(Builders<OfflineNotification>.Filter.AnyEq(n => n.ReadByUserIds, Alice)).Project(n => n.Id).ToListAsync();
            readByAlice.Should().BeEquivalentTo("own", "org-1-wide");
        }

        [Fact]
        public async Task MarkAsRead_OfARowTheViewerCannotSee_ChangesNothing()
        {
            await SeedAsync(_fixture.Dev, Row("bobs", userId: Bob), Row("org-2-wide", organizationId: "org-2"));

            await _repository.MarkAsReadAsync(Viewer(Alice, Org1), "bobs");
            await _repository.MarkAsReadAsync(Viewer(Alice, Org1), "org-2-wide");

            (await _fixture.Dev.GetCollection<OfflineNotification>(Collection)
                .CountDocumentsAsync(Builders<OfflineNotification>.Filter.AnyEq(n => n.ReadByUserIds, Alice))).Should().Be(0);
        }

        [Fact]
        public async Task OrganizationRow_NeverShowsWhoElseReadIt()
        {
            await SeedAsync(_fixture.Dev, Row("org-1-wide", organizationId: Org1));
            await _repository.MarkAsReadAsync(Viewer(Bob, Org1), "org-1-wide");
            await _repository.MarkAsReadAsync(Viewer(Alice, Org1), "org-1-wide");

            var page = await _repository.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 10 }, Viewer(Alice, Org1));

            var row = page.Notifications.Single();
            row.IsRead.Should().BeTrue();
            row.ReadByUserIds.Should().Equal(Alice);
        }

        [Fact]
        public async Task ImpersonatedRootUser_SeesTheProjectsConsoleRowsInRoot_NotOtherProjects()
        {
            TestBlocksContext.SetImpersonated("dev", "root", "root-user");
            await SeedAsync(_fixture.Main,
                Row("this-project", userId: "root-user", tenantId: "dev"),
                Row("no-project", userId: "root-user"),
                Row("other-project", userId: "root-user", tenantId: "other"));

            var ids = await IdsAsync(new InboxViewer("root-user", [], "dev"));

            ids.Should().Equal("no-project", "this-project");
        }

        [Fact]
        public async Task LegacyBroadcastRows_AreMarkedByMarkAll_ButPersonalRowsOfTheBroadcastTypeAreNot()
        {
            var legacy = Row("legacy-broadcast");
            legacy.Payload.NotificationType = "BroadcastReceiverType";
            var someoneElses = Row("bob-broadcast-type", userId: Bob);
            someoneElses.Payload.NotificationType = "BroadcastReceiverType";
            await SeedAsync(_fixture.Dev, legacy, someoneElses);

            await _repository.MarkAllAsReadAsync(Viewer(Alice, Org1));

            var readByAlice = await _fixture.Dev.GetCollection<OfflineNotification>(Collection)
                .Find(Builders<OfflineNotification>.Filter.AnyEq(n => n.ReadByUserIds, Alice)).Project(n => n.Id).ToListAsync();
            readByAlice.Should().Equal("legacy-broadcast");
        }
    }
}
