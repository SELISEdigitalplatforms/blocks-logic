using DomainService.Configuration.Services;
using DomainService.Entities;
using DomainService.Notification;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using XUnitTest.TestHelpers;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.Shared.Entities;
using DomainService.Shared;
using Mail.DomainService.Entities;
using MailBoxSyncService.Services;

namespace XUnitTest.Routing;

public class NotificationPlacementTests : IDisposable
{
    private readonly TenantPlacementFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task NotificationListingAndReadStatus_IncludeTenantAndRootPlacements()
    {
        TestBlocksContext.Set("dev", "same-user");
        var repository = new NotificationRepository(_fixture.Provider, _fixture.Secret, NullLogger<NotificationRepository>.Instance);
        var collectionName = "OfflineNotifications";
        await _fixture.Dev.GetCollection<OfflineNotification>(collectionName).InsertOneAsync(new OfflineNotification
        {
            Id = "dev-note", Payload = new PayloadData { UserId = "same-user" },
            ReadByUserIds = [], CreatedTime = DateTime.UtcNow.AddMinutes(-1)
        });
        await _fixture.Main.GetCollection<OfflineNotification>(collectionName).InsertOneAsync(new OfflineNotification
        {
            Id = "root-note", Payload = new PayloadData { UserId = "same-user" },
            ReadByUserIds = [], CreatedTime = DateTime.UtcNow
        });
        await _fixture.Other.GetCollection<OfflineNotification>(collectionName).InsertOneAsync(new OfflineNotification
        {
            Id = "other-note", Payload = new PayloadData { UserId = "same-user" },
            ReadByUserIds = [], CreatedTime = DateTime.UtcNow
        });

        var page = await repository.GetNotificationsAsync(new GetNotificationsRequest { Page = 0, PageSize = 1 });
        Assert.Equal("root-note", Assert.Single(page.Notifications).Id);
        Assert.Equal(2, page.TotalNotificationsCount);
        Assert.Equal(2, page.UnReadNotificationsCount);
        var secondPage = await repository.GetNotificationsAsync(new GetNotificationsRequest { Page = 1, PageSize = 1 });
        Assert.Equal("dev-note", Assert.Single(secondPage.Notifications).Id);
        var filtered = await repository.GetNotificationItemsAcrossPlacementsAsync(n => n.Payload.UserId == "same-user");
        Assert.Equal(new[] { "dev-note", "root-note" }, filtered.Select(n => n.Id).OrderBy(id => id));

        await repository.UpdateNotificationAsReadByUserIdAsync("same-user", "root-note");
        var rootNote = await _fixture.Main.GetCollection<OfflineNotification>(collectionName)
            .Find(n => n.Id == "root-note").SingleAsync();
        Assert.Contains("same-user", rootNote.ReadByUserIds);

        await repository.UpdateNotificationAsReadByUserIdAsync("same-user");
        var devNote = await _fixture.Dev.GetCollection<OfflineNotification>(collectionName)
            .Find(n => n.Id == "dev-note").SingleAsync();
        Assert.Contains("same-user", devNote.ReadByUserIds);
        Assert.Equal(0, await _fixture.Other.GetCollection<OfflineNotification>(collectionName)
            .CountDocumentsAsync(n => n.ReadByUserIds.Contains("same-user")));
    }

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
