using Blocks.Genesis;
using DomainService.Notification.Push;
using FluentAssertions;
using MongoDB.Driver;
using XUnitTest.Routing;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification.Push
{
    /// <summary>
    /// Device API rules on local MongoDB: own devices only, at most 10, impersonated root users in the ROOT DB, the
    /// device name, and devices not seen for 60 days removed (TTL index) and never read for sending.
    /// </summary>
    public sealed class PushDeviceTests : IDisposable
    {
        private readonly TenantPlacementFixture _fixture = new();
        private readonly PushDeviceStore _store;
        private readonly PushDeviceService _sut;
        private readonly SteppingTime _time = new();

        private sealed class SteppingTime : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _now = _now.AddSeconds(1);
        }

        public PushDeviceTests()
        {
            _store = new PushDeviceStore(_fixture.Provider, _fixture.Secret);
            _sut = new PushDeviceService(_store, _time);
            TestBlocksContext.Set("dev", "alice");
        }

        public void Dispose() => _fixture.Dispose();

        private static IMongoCollection<NotificationDevice> Devices(IMongoDatabase database) =>
            database.GetCollection<NotificationDevice>(NotificationDevice.CollectionName);

        private Task RegisterAsync(string token, string platform = "android") =>
            _sut.RegisterAsync(new RegisterDeviceRequest { Token = token, Platform = platform }, CancellationToken.None);

        [Fact]
        public async Task TheSameToken_IsStoredOnce_AndMovesToTheUserWhoRegistersItLast()
        {
            await RegisterAsync("token-1");
            TestBlocksContext.Set("dev", "bob");
            await RegisterAsync("token-1", "ios");

            var stored = await Devices(_fixture.Dev).Find(FilterDefinition<NotificationDevice>.Empty).ToListAsync();
            stored.Should().ContainSingle().Which.Should().Match<NotificationDevice>(d => d.UserId == "bob" && d.Platform == "ios");
        }

        [Fact]
        public async Task AnEleventhDevice_RemovesTheOldestSeen()
        {
            for (var i = 1; i <= PushDeviceService.MaxDevicesPerUser + 1; i++)
                await RegisterAsync($"token-{i}");

            var tokens = await Devices(_fixture.Dev).Find(d => d.UserId == "alice").Project(d => d.Token).ToListAsync();
            tokens.Should().HaveCount(PushDeviceService.MaxDevicesPerUser).And.NotContain("token-1");
        }

        [Fact]
        public async Task Remove_DeletesOnlyTheCallersOwnToken()
        {
            TestBlocksContext.Set("dev", "bob");
            await RegisterAsync("bobs-token");
            TestBlocksContext.Set("dev", "alice");

            (await _sut.RemoveAsync("bobs-token", CancellationToken.None)).Should().BeFalse();
            (await Devices(_fixture.Dev).CountDocumentsAsync(FilterDefinition<NotificationDevice>.Empty)).Should().Be(1);

            TestBlocksContext.Set("dev", "bob");
            (await _sut.RemoveAsync("bobs-token", CancellationToken.None)).Should().BeTrue();
        }

        [Fact]
        public async Task AnImpersonatedRootUser_RegistersInTheRootDb()
        {
            TestBlocksContext.SetImpersonated("dev", "root", "root-user");

            await RegisterAsync("root-token");

            (await Devices(_fixture.Main).CountDocumentsAsync(d => d.UserId == "root-user")).Should().Be(1);
            (await Devices(_fixture.Dev).CountDocumentsAsync(FilterDefinition<NotificationDevice>.Empty)).Should().Be(0);
        }

        [Theory]
        [InlineData("", "android", "Token")]
        [InlineData("t", "windows", "Platform")]
        [InlineData(null, null, "Token")]
        public async Task ABadRequest_IsRefusedAndStoresNothing(string? token, string? platform, string field)
        {
            var result = await _sut.RegisterAsync(new RegisterDeviceRequest { Token = token, Platform = platform }, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(field);
            (await Devices(_fixture.Dev).CountDocumentsAsync(FilterDefinition<NotificationDevice>.Empty)).Should().Be(0);
        }

        [Fact]
        public async Task ATokenOverTheLimit_IsRefused()
        {
            var result = await _sut.RegisterAsync(new RegisterDeviceRequest
            {
                Token = new string('t', PushDeviceService.MaxTokenLength + 1),
                Platform = "web",
            }, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task TheDedupStore_ClaimsAnIdOnce()
        {
            var dedup = new PushDedupStore(_fixture.Provider, _fixture.Secret, TimeProvider.System);
            var id = Guid.NewGuid();

            (await dedup.TryClaimAsync(id, CancellationToken.None)).Should().BeTrue();
            (await dedup.TryClaimAsync(id, CancellationToken.None)).Should().BeFalse();
            (await dedup.TryClaimAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeTrue();
        }

        [Fact]
        public async Task AReleasedClaim_CanBeClaimedAgain()
        {
            var dedup = new PushDedupStore(_fixture.Provider, _fixture.Secret, TimeProvider.System);
            var id = Guid.NewGuid();
            await dedup.TryClaimAsync(id, CancellationToken.None);

            await dedup.ReleaseAsync(id, CancellationToken.None);

            (await dedup.TryClaimAsync(id, CancellationToken.None)).Should().BeTrue();
        }

        [Fact]
        public async Task TheDeviceName_IsStored_AndAnOverlongNameRefused()
        {
            await _sut.RegisterAsync(new RegisterDeviceRequest { Token = "token-1", Platform = "ios", Name = " iPhone 15 " }, CancellationToken.None);
            var refused = await _sut.RegisterAsync(new RegisterDeviceRequest
            {
                Token = "token-2", Platform = "ios", Name = new string('n', PushDeviceService.MaxNameLength + 1),
            }, CancellationToken.None);

            (await Devices(_fixture.Dev).Find(d => d.Token == "token-1").SingleAsync()).Name.Should().Be("iPhone 15");
            refused.Errors.Should().ContainKey("Name");
            (await Devices(_fixture.Dev).CountDocumentsAsync(d => d.Token == "token-2")).Should().Be(0);
        }

        [Fact]
        public async Task Bug_OldDevicesWereNeverCleaned_NowATtlIndexRemovesThemAfter60Days()
        {
            await RegisterAsync("token-1");

            var indexes = await (await Devices(_fixture.Dev).Indexes.ListAsync()).ToListAsync();
            var ttl = indexes.Single(i => i["name"] == PushDeviceStore.TtlIndexName);
            ttl["key"].AsBsonDocument.Names.Should().Equal(nameof(NotificationDevice.LastSeenAt));
            ttl["expireAfterSeconds"].ToInt64().Should().Be((long)TimeSpan.FromDays(60).TotalSeconds);
        }

        [Fact]
        public async Task ADeviceNotSeenFor60Days_IsNeverReadForSending()
        {
            var now = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
            await Devices(_fixture.Dev).InsertManyAsync(
            [
                new NotificationDevice { Id = "old", UserId = "alice", Token = "old", Platform = "android", LastSeenAt = now.AddDays(-61) },
                new NotificationDevice { Id = "new", UserId = "alice", Token = "new", Platform = "android", LastSeenAt = now.AddDays(-59) },
            ]);

            var devices = await _store.GetForUsersAsync(new PushDb("dev", false), ["alice"], NotificationDevice.SeenSince(now), CancellationToken.None);

            devices.Select(d => d.Token).Should().Equal("new");
        }
    }
}
