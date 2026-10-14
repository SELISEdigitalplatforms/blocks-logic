using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;
using CloudRepository = CloudConfiguration.DomainService.Shared.Services.ConfigurationRepository;
using CloudListRequest = CloudConfiguration.DomainService.Notification.RequestModel.GetNotificationConfigurationsRequest;
using SendRepository = DomainService.Configuration.Services.ConfigurationRepository;
using CloudConfig = Blocks.Genesis.Entities.Os.NotificationConfiguration;
using SendConfig = Blocks.Genesis.Entities.Os.NotificationConfiguration;

namespace XUnitTest.Routing;

/// <summary>
/// SPEC1 3.3: notification configurations move to Genesis KeyValueStores. Real local Mongo for the old
/// collection, a mocked <see cref="IKeyValueStore"/> for the new one. The mock records the
/// <c>impersonated</c> flag so the routing (root vs tenant DB) is asserted.
/// </summary>
public class NotificationConfigKeyValueTests : IDisposable
{
    private const string Prefix = "notification-config:";
    private const string Old = "NotificationConfigurations";

    private readonly TenantPlacementFixture _fixture = new();
    private readonly Mock<IKeyValueStore> _kv = new();

    public void Dispose() => _fixture.Dispose();

    private CloudRepository Cloud() => new(_fixture.Provider, _fixture.Secret, _kv.Object);

    private SendRepository Send() =>
        new(_fixture.Provider, _fixture.Secret, NullLogger<SendRepository>.Instance, _kv.Object);

    private static async Task<List<string>> CollectionNames(IMongoDatabase db) =>
        await (await db.ListCollectionNamesAsync()).ToListAsync();

    private static CloudConfig CloudCfg(string id, string name, DateTime created) =>
        new() { ItemId = id, Name = name, CreatedDate = created };

    // ---------------- CloudConfiguration: list ----------------

    [Fact]
    public async Task List_OldCollectionPresent_UsesTheOldPath_AndNeverCallsKv()
    {
        TestBlocksContext.Set("dev");
        await _fixture.Dev.GetCollection<CloudConfig>(Old).InsertOneAsync(CloudCfg("a", "old-one", DateTime.UtcNow));

        var result = await Cloud().GetNotificationConfigurationsAsync(new CloudListRequest { Page = 0, PageSize = 10 });

        result.IsSuccess.Should().BeTrue();
        result.TotalCount.Should().Be(1);
        result.Configurations.Single().Name.Should().Be("old-one");
        _kv.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task List_OldCollectionGone_ReadsKvByPrefix_SortedPagedCounted_AndCreatesNothing()
    {
        TestBlocksContext.Set("dev");
        var now = DateTime.UtcNow;
        _kv.Setup(k => k.GetByPrefixAsync<CloudConfig>(Prefix, true, default)).ReturnsAsync(new List<CloudConfig>
        {
            CloudCfg("1", "oldest", now.AddMinutes(-3)),
            CloudCfg("3", "newest", now),
            CloudCfg("2", "middle", now.AddMinutes(-1)),
        });

        var page0 = await Cloud().GetNotificationConfigurationsAsync(new CloudListRequest { Page = 0, PageSize = 2 });
        var page1 = await Cloud().GetNotificationConfigurationsAsync(new CloudListRequest { Page = 1, PageSize = 2 });

        page0.TotalCount.Should().Be(3);
        page0.Configurations.Select(c => c.Name).Should().Equal("newest", "middle");
        page1.Configurations.Select(c => c.Name).Should().Equal("oldest");
        page1.TotalCount.Should().Be(3);
        (await CollectionNames(_fixture.Dev)).Should().NotContain(Old);
    }

    [Fact]
    public async Task List_Impersonated_OldCollectionGoneInRoot_ReadsRootKv()
    {
        TestBlocksContext.SetImpersonated("dev", "root");
        _kv.Setup(k => k.GetByPrefixAsync<CloudConfig>(Prefix, false, default))
            .ReturnsAsync(new List<CloudConfig> { CloudCfg("r", "root-cfg", DateTime.UtcNow) });

        var result = await Cloud().GetNotificationConfigurationsAsync(new CloudListRequest { Page = 0, PageSize = 10 });

        result.Configurations.Single().Name.Should().Be("root-cfg");
        _kv.Verify(k => k.GetByPrefixAsync<CloudConfig>(Prefix, false, default), Times.Once);
        _kv.Verify(k => k.GetByPrefixAsync<CloudConfig>(Prefix, true, default), Times.Never);
    }

    [Fact]
    public async Task List_OldCollectionGone_PageSizeZero_ReturnsAllRows_LikeMongoLimitZero()
    {
        TestBlocksContext.Set("dev");
        var now = DateTime.UtcNow;
        _kv.Setup(k => k.GetByPrefixAsync<CloudConfig>(Prefix, true, default)).ReturnsAsync(new List<CloudConfig>
        {
            CloudCfg("1", "a", now.AddMinutes(-1)), CloudCfg("2", "b", now)
        });

        var result = await Cloud().GetNotificationConfigurationsAsync(new CloudListRequest { Page = 0, PageSize = 0 });

        result.Configurations.Select(c => c.Name).Should().Equal("b", "a");
        result.TotalCount.Should().Be(2);
    }

    // ---------------- CloudConfiguration: get by id ----------------

    [Fact]
    public async Task GetById_OldCollectionPresent_UsesTheOldPath()
    {
        TestBlocksContext.Set("dev");
        await _fixture.Dev.GetCollection<CloudConfig>(Old).InsertOneAsync(CloudCfg("a", "old-one", DateTime.UtcNow));

        var found = await Cloud().GetNotificationConfigurationByIdAsync("a");

        found.Name.Should().Be("old-one");
        _kv.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_OldCollectionGone_FindsTheItemIdInKv_AndMissReturnsNull()
    {
        TestBlocksContext.Set("dev");
        _kv.Setup(k => k.GetByPrefixAsync<CloudConfig>(Prefix, true, default)).ReturnsAsync(new List<CloudConfig>
        {
            CloudCfg("1", "one", DateTime.UtcNow), CloudCfg("2", "two", DateTime.UtcNow)
        });

        (await Cloud().GetNotificationConfigurationByIdAsync("2")).Name.Should().Be("two");
        (await Cloud().GetNotificationConfigurationByIdAsync("nope")).Should().BeNull();
        (await CollectionNames(_fixture.Dev)).Should().NotContain(Old);
    }

    [Fact]
    public async Task CloudRepository_NeverWritesKv()
    {
        TestBlocksContext.Set("dev");
        var repo = Cloud();
        await repo.SaveNotificationConfigurationAsync(CloudCfg("w", "written", DateTime.UtcNow));
        await repo.GetNotificationConfigurationByNameAsync("written");
        await repo.DeleteNotificationConfigurationAsync(new() { ItemId = "w" });

        _kv.Verify(k => k.SetAsync(It.IsAny<string>(), It.IsAny<CloudConfig>(), It.IsAny<bool>(), default), Times.Never);
        _kv.Verify(k => k.DeleteAsync(It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
        _kv.Invocations.Should().BeEmpty();
    }

    // ---------------- Send-time repo: get by name ----------------

    [Fact]
    public async Task SendTime_KvHit_ReturnsIt_AndNeverReadsTheOldCollection()
    {
        TestBlocksContext.Set("dev");
        // An old doc with the same name and different data: if the old collection were read, it would win.
        await _fixture.Dev.GetCollection<SendConfig>(Old).InsertOneAsync(new SendConfig { ItemId = "old", Name = "welcome", NotifyMethod = "old-method" });
        _kv.Setup(k => k.GetAsync<SendConfig>(Prefix + "welcome", true, default))
            .ReturnsAsync(new SendConfig { ItemId = "kv", Name = "welcome", NotifyMethod = "kv-method" });

        var result = await Send().GetByNameAsync("welcome");

        result.NotifyMethod.Should().Be("kv-method");
    }

    [Fact]
    public async Task SendTime_KvMiss_ReadsTheOldCollection()
    {
        TestBlocksContext.Set("dev");
        await _fixture.Dev.GetCollection<SendConfig>(Old).InsertOneAsync(new SendConfig { ItemId = "old", Name = "welcome", NotifyMethod = "old-method" });

        var result = await Send().GetByNameAsync("welcome");

        result.NotifyMethod.Should().Be("old-method");
        _kv.Verify(k => k.GetAsync<SendConfig>(Prefix + "welcome", true, default), Times.Once);
    }

    [Fact]
    public async Task SendTime_KvMissAndOldCollectionGone_ReturnsNull_CreatesNothing_NoCollectionListing()
    {
        TestBlocksContext.Set("dev");

        var result = await Send().GetByNameAsync("welcome");

        result.Should().BeNull();
        (await CollectionNames(_fixture.Dev)).Should().NotContain(Old);
        (await CollectionNames(_fixture.Main)).Should().NotContain(Old);
        // The KV read is the only store call; there is no live "does the collection exist" check on this path.
        _kv.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task SendTime_Impersonated_ReadsRootKv_AndRootOldCollection()
    {
        TestBlocksContext.SetImpersonated("dev", "root");
        await _fixture.Main.GetCollection<SendConfig>(Old).InsertOneAsync(new SendConfig { ItemId = "root-old", Name = "welcome", NotifyMethod = "root-old" });

        var result = await Send().GetByNameAsync("welcome");

        result.NotifyMethod.Should().Be("root-old");
        _kv.Verify(k => k.GetAsync<SendConfig>(Prefix + "welcome", false, default), Times.Once);
        _kv.Verify(k => k.GetAsync<SendConfig>(It.IsAny<string>(), true, default), Times.Never);
    }

    [Fact]
    public async Task SendTime_KeyIsTrimmed()
    {
        TestBlocksContext.Set("dev");
        _kv.Setup(k => k.GetAsync<SendConfig>(Prefix + "welcome", true, default))
            .ReturnsAsync(new SendConfig { Name = "welcome", NotifyMethod = "kv" });

        (await Send().GetByNameAsync("  welcome ")).NotifyMethod.Should().Be("kv");
    }

    [Fact]
    public async Task SendTime_NameNull_UsesTheEmptyKey_AndReturnsNullWithoutThrowing()
    {
        TestBlocksContext.Set("dev");

        var result = await Send().GetByNameAsync(null!);

        result.Should().BeNull();
        _kv.Verify(k => k.GetAsync<SendConfig>(Prefix, true, default), Times.Once);
    }

    [Fact]
    public async Task SendTime_NoContext_Throws_BeforeAnyRead()
    {
        TestBlocksContext.Clear();

        var act = () => Send().GetByNameAsync("welcome");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _kv.Invocations.Should().BeEmpty();
        (await CollectionNames(_fixture.Main)).Should().NotContain(Old);
    }

    [Fact]
    public async Task SendTime_KvError_SurfacesUnchanged()
    {
        TestBlocksContext.Set("dev");
        _kv.Setup(k => k.GetAsync<SendConfig>(It.IsAny<string>(), It.IsAny<bool>(), default))
            .ThrowsAsync(new TimeoutException("mongo"));

        var act = () => Send().GetByNameAsync("welcome");

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task SendTime_SaveAndDelete_NeverTouchKv()
    {
        TestBlocksContext.Set("dev");
        var repo = Send();
        await repo.SaveAsync(new SendConfig { ItemId = "s", Name = "saved" });
        await repo.DeleteConfigurationAsync(new() { ItemId = "s" });

        _kv.Invocations.Should().BeEmpty();
    }
}
