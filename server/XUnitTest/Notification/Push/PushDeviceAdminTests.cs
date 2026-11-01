using DomainService.Notification.Push;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.Routing;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification.Push
{
    /// <summary>
    /// Admin device list and remove, and the push status counts, on local MongoDB: this tenant's DB only (never the
    /// ROOT DB or another tenant), search taken literally, paging capped, old devices left out.
    /// </summary>
    public sealed class PushDeviceAdminTests : IDisposable
    {
        private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        private readonly TenantPlacementFixture _fixture = new();
        private readonly PushDeviceStore _store;
        private readonly PushDeviceAdminService _sut;
        private readonly Mock<IPushStatusStore> _status = new();
        private readonly PushStatusService _statusService;

        private sealed class FixedTime : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }

        public PushDeviceAdminTests()
        {
            _store = new PushDeviceStore(_fixture.Provider, _fixture.Secret);
            var context = new PushDeviceService(_store, new FixedTime());
            _sut = new PushDeviceAdminService(context, _store, new PushUserRepository(_fixture.Provider), new FixedTime());
            _statusService = new PushStatusService(context, _store, _status.Object, new FixedTime());
            TestBlocksContext.Set("dev", "admin");
        }

        public void Dispose() => _fixture.Dispose();

        private async Task SeedAsync()
        {
            await _fixture.Dev.GetCollection<BsonDocument>("Users").InsertManyAsync(
            [
                new BsonDocument { ["_id"] = "ada", ["FirstName"] = "Ada", ["LastName"] = "Lovelace", ["Email"] = "ada@example.com" },
                new BsonDocument { ["_id"] = "bob", ["UserName"] = "bob.builder", ["Email"] = "bob@example.com" },
            ]);
            await Devices(_fixture.Dev).InsertManyAsync(
            [
                Device("d1", "ada", "android", Now.AddHours(-1)),
                Device("d2", "ada", "ios", Now.AddHours(-2)),
                Device("d3", "bob", "web", Now.AddHours(-3)),
                Device("d4", "bob", "android", Now.AddDays(-61)),
            ]);
            await Devices(_fixture.Main).InsertOneAsync(Device("r1", "root-user", "android", Now));
            await Devices(_fixture.Other).InsertOneAsync(Device("o1", "other-user", "android", Now));
        }

        private static NotificationDevice Device(string token, string userId, string platform, DateTime lastSeen) => new()
        {
            Id = NotificationDevice.IdOf(token), UserId = userId, Token = token, Platform = platform, LastSeenAt = lastSeen,
        };

        private static IMongoCollection<NotificationDevice> Devices(IMongoDatabase database) =>
            database.GetCollection<NotificationDevice>(NotificationDevice.CollectionName);

        private Task<PushDeviceList> ListAsync(string? platform = null, string? search = null, int page = 1, int pageSize = 20) =>
            _sut.ListAsync(new PushDeviceListRequest { Platform = platform, Search = search, Page = page, PageSize = pageSize }, CancellationToken.None);

        [Fact]
        public async Task TheList_HoldsThisTenantsRecentDevicesOnly_NewestFirst_WithUserNames()
        {
            await SeedAsync();

            var list = await ListAsync();

            list.Items.Select(i => (i.UserName, i.Platform)).Should().Equal(("Ada Lovelace", "android"), ("Ada Lovelace", "ios"), ("bob.builder", "web"));
            list.TotalCount.Should().Be(3);
            list.Counts.Should().Be(new PushDeviceCounts(3, 1, 1, 1));
        }

        [Fact]
        public async Task ThePlatformFilter_LimitsItems_ButCountsStayPerPlatform()
        {
            await SeedAsync();

            var list = await ListAsync(platform: "IOS");

            list.Items.Should().ContainSingle().Which.Platform.Should().Be("ios");
            list.Counts.All.Should().Be(3);
        }

        [Theory]
        [InlineData("LOVELACE", 2)]
        [InlineData("ada love", 2)]
        [InlineData("bob@example", 1)]
        [InlineData(".*", 0)]
        public async Task TheSearch_IsCaseInsensitive_AndTakenLiterally(string search, int expected)
        {
            await SeedAsync();

            var list = await ListAsync(search: search);

            list.TotalCount.Should().Be(expected);
            list.Counts.All.Should().Be(expected);
        }

        [Fact]
        public async Task ThePageSize_IsCappedAt100()
        {
            await Devices(_fixture.Dev).InsertManyAsync(Enumerable.Range(0, 105).Select(i => Device("t" + i, "ada", "android", Now)));

            var list = await ListAsync(pageSize: 500);

            list.Items.Should().HaveCount(PushDeviceAdminService.MaxPageSize);
            list.TotalCount.Should().Be(105);
        }

        [Fact]
        public async Task Remove_DeletesAnyDeviceOfThisTenant_ButNeverRootOrOtherTenantRows()
        {
            await SeedAsync();

            (await _sut.RemoveAsync(NotificationDevice.IdOf("d3"), CancellationToken.None)).Should().BeTrue();
            (await _sut.RemoveAsync(NotificationDevice.IdOf("r1"), CancellationToken.None)).Should().BeFalse();
            (await _sut.RemoveAsync(NotificationDevice.IdOf("o1"), CancellationToken.None)).Should().BeFalse();
            (await _sut.RemoveAsync("not-an-id", CancellationToken.None)).Should().BeFalse();

            (await Devices(_fixture.Main).CountDocumentsAsync(FilterDefinition<NotificationDevice>.Empty)).Should().Be(1);
            (await Devices(_fixture.Other).CountDocumentsAsync(FilterDefinition<NotificationDevice>.Empty)).Should().Be(1);
        }

        [Fact]
        public async Task TheStatus_CountsThisTenantsRecentDevices_AndShowsTheLastTest()
        {
            await SeedAsync();
            var lastTest = new PushLastTest { At = Now, Delivered = 0, Failed = 1, Reason = PushTestCodes.ApnsKeyMissing };
            _status.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PushStatusRecord { Ios = lastTest });

            var status = await _statusService.GetAsync(CancellationToken.None);

            status.Should().Be(new PushStatusView(new PushPlatformStatus(1, null), new PushPlatformStatus(1, lastTest), new PushPlatformStatus(1, null)));
        }
    }
}
