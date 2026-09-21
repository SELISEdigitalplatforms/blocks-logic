using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Notification;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using XUnitTest.TestHelpers;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.Shared.Entities;
using Mail.DomainService.Entities;
using MailBoxSyncService.Services;

namespace XUnitTest.Routing;

public class NotificationPlacementTests : IDisposable
{
    private readonly TenantPlacementFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task SharedRepositories_ConstructWithoutContext_AndKeepConcurrentTenantWritesSeparate()
    {
        TestBlocksContext.Clear();
        var configuration = new ConfigurationRepository(_fixture.Provider, _fixture.Secret, NullLogger<ConfigurationRepository>.Instance);
        var notifications = new NotificationRepository(_fixture.Provider, _fixture.Secret, NullLogger<NotificationRepository>.Instance);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Write(string tenant)
        {
            TestBlocksContext.Set(tenant);
            await gate.Task;
            for (var i = 0; i < 12; i++)
            {
                await configuration.SaveAsync(new NotificationConfiguration { ItemId = "same-" + i, Name = tenant });
                await notifications.SaveAsync(new NotificationConfiguration { ItemId = "same-" + i, Name = tenant }, "RoutingNotifications");
            }
        }
        var dev = Write("dev");
        var other = Write("other");
        gate.SetResult();
        await Task.WhenAll(dev, other);
        foreach (var tenant in new[] { "dev", "other" })
        {
            TestBlocksContext.Set(tenant);
            Assert.Equal(tenant, (await configuration.GetByIdAsync("same-0")).Name);
            foreach (var name in new[] { "NotificationConfigurations", "RoutingNotifications" })
            {
                var collection = _fixture.Provider.GetDatabase(tenant).GetCollection<NotificationConfiguration>(name);
                Assert.Equal(12, await collection.CountDocumentsAsync(c => c.Name == tenant));
                Assert.Equal(0, await collection.CountDocumentsAsync(c => c.Name != tenant));
                Assert.Equal(0, await _fixture.Main.GetCollection<NotificationConfiguration>(name).CountDocumentsAsync(c => true));
            }
        }
    }

    [Fact]
    public async Task Configuration_FollowsRefreshedTenantPlacement_OnTheSameRepository()
    {
        TestBlocksContext.Set("dev");
        var repository = new ConfigurationRepository(_fixture.Provider, _fixture.Secret, NullLogger<ConfigurationRepository>.Instance);
        var oldDatabase = _fixture.Dev;
        await repository.SaveAsync(new NotificationConfiguration { ItemId = "same", Name = "before" });
        _fixture.Tenants.Setup(t => t.GetTenantDatabaseConnectionString("dev"))
            .Returns((_fixture.Other.DatabaseNamespace.DatabaseName, _fixture.Secret.OtherDatabaseConnectionString));
        await repository.SaveAsync(new NotificationConfiguration { ItemId = "same", Name = "after" });
        Assert.Equal("after", (await repository.GetByIdAsync("same")).Name);
        Assert.Equal("before", (await oldDatabase.GetCollection<NotificationConfiguration>("NotificationConfigurations")
            .Find(c => c.ItemId == "same").SingleAsync()).Name);
    }

    [Fact]
    public async Task MissingContext_RejectsConfigurationWrites_WithoutCreatingRootRecords()
    {
        TestBlocksContext.Clear();
        var repository = new ConfigurationRepository(_fixture.Provider, _fixture.Secret, NullLogger<ConfigurationRepository>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(new NotificationConfiguration { ItemId = "missing" }));
        Assert.Equal(0, await _fixture.Main.GetCollection<NotificationConfiguration>("NotificationConfigurations").CountDocumentsAsync(c => true));
    }

    [Fact]
    public async Task ManagedServiceAndMailboxOperations_KeepTheirSelectedTenant()
    {
        var services = new ServiceManagementRepository(_fixture.Provider, _fixture.Secret);
        var mailbox = new MailRepository(_fixture.Provider, _fixture.Secret);
        foreach (var tenant in new[] { "dev", "other" })
        {
            await _fixture.Provider.GetDatabase(tenant).GetCollection<BlocksManagedService>("BlocksManagedServices")
                .InsertOneAsync(new BlocksManagedService { ItemId = "same", TenantId = tenant, Name = tenant });
        }
        await Task.WhenAll(new[] { "dev", "other" }.Select(async tenant =>
        {
            TestBlocksContext.Set(tenant);
            var (items, count) = await services.GetAllServicesAsync(new GetAllServiceRequest { PageSize = 10 });
            Assert.Equal(1, count);
            Assert.Equal(tenant, Assert.Single(items).Name);
            await mailbox.InsertAsync(new MailBoxEntity { ItemId = "same", MessageId = tenant }, tenant);
            Assert.True(await mailbox.ExistsAsync(tenant, tenant));
            Assert.False(await mailbox.ExistsAsync(tenant == "dev" ? "other" : "dev", tenant));
        }));
        Assert.Equal(0, await _fixture.Main.GetCollection<MailBoxEntity>("MailBoxEntitys").CountDocumentsAsync(m => true));
        Assert.Equal(0, await _fixture.Main.GetCollection<BlocksManagedService>("BlocksManagedServices").CountDocumentsAsync(m => true));
    }
}
