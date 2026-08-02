using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using XUnitTest.Integration;
using XUnitTest.TestHelpers;
using ConfigurationRepository = DomainService.Configuration.Services.ConfigurationRepository;
using GetConfigurationsRequest = DomainService.Configuration.GetConfigurationsRequest;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Integration tests for the notification <see cref="ConfigurationRepository"/>. They run
    /// against the local MongoDB in a throwaway database of their own, because the upsert and the
    /// paging are server side behaviour.
    /// </summary>
    [Collection("Mongo integration")]
    public class NotificationConfigurationRepositoryIntegrationTests : IDisposable
    {
        private const string CollectionName = "NotificationConfigurations";

        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly string _databaseName;
        private readonly ConfigurationRepository _sut;

        public NotificationConfigurationRepositoryIntegrationTests(MongoIntegrationFixture fixture)
        {
            TestBlocksContext.Set("tenant-notif-cfg", "user-notif-cfg");

            _client = fixture.Client;
            _databaseName = "blocks_logic_notif_cfg_" + Guid.NewGuid().ToString("N");
            _database = _client.GetDatabase(_databaseName);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns(_database);
            provider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>())).Returns(_database);

            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns(MongoIntegrationFixture.ConnectionString);

            _sut = new ConfigurationRepository(
                provider.Object, secret.Object, Mock.Of<ILogger<ConfigurationRepository>>());
        }

        public void Dispose()
        {
            try
            {
                _client.DropDatabase(_databaseName);
            }
            catch (MongoException)
            {
                // best effort cleanup; never mask the real test outcome
            }

            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static NotificationConfiguration Configuration(
            string itemId, string name, DateTime createdDate) => new()
            {
                ItemId = itemId,
                Name = name,
                ChannelToNotify = NotifierTypes.SignalR,
                NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
                NotifyMethod = "ReceiveNotification",
                EnablePersistence = true,
                CreatedDate = createdDate,
                CreatedBy = "user-notif-cfg",
            };

        private Task<long> StoredCountAsync() =>
            _database.GetCollection<NotificationConfiguration>(CollectionName)
                     .CountDocumentsAsync(FilterDefinition<NotificationConfiguration>.Empty);

        [Fact]
        public async Task SaveAsync_StoresANewConfiguration()
        {
            await _sut.SaveAsync(Configuration("cfg-1", "welcome", DateTime.UtcNow));

            var stored = await _sut.GetByNameAsync("welcome");
            stored.Should().NotBeNull();
            stored.ItemId.Should().Be("cfg-1");
            stored.NotifyMethod.Should().Be("ReceiveNotification");
            stored.EnablePersistence.Should().BeTrue();
        }

        [Fact]
        public async Task SaveAsync_ReplacesTheConfigurationThatCarriesTheSameItemId()
        {
            var configuration = Configuration("cfg-1", "welcome", DateTime.UtcNow);
            await _sut.SaveAsync(configuration);

            configuration.NotifyMethod = "OrderChanged";
            configuration.EnablePersistence = false;
            await _sut.SaveAsync(configuration);

            (await StoredCountAsync()).Should().Be(1, "an existing configuration is replaced, not duplicated");
            var stored = await _sut.GetByIdAsync("cfg-1");
            stored.NotifyMethod.Should().Be("OrderChanged");
            stored.EnablePersistence.Should().BeFalse();
        }

        [Fact]
        public async Task GetByNameAsync_ReachesTheRootDatabaseForAnImpersonatedCaller()
        {
            ImpersonatedTestContext.Set("tenant-notif-cfg", "user-notif-cfg").Should().BeTrue();

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>())).Returns(_database);
            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns(MongoIntegrationFixture.ConnectionString);

            var repository = new ConfigurationRepository(
                provider.Object, secret.Object, Mock.Of<ILogger<ConfigurationRepository>>());
            await repository.SaveAsync(Configuration("cfg-impersonated", "impersonated", DateTime.UtcNow));

            // The database is resolved once in the constructor and again on every name lookup.
            (await repository.GetByNameAsync("impersonated")).Should().NotBeNull();
            provider.Verify(p => p.GetDatabase(MongoIntegrationFixture.ConnectionString, "BlocksRootDb"),
                Times.Exactly(2));
            provider.Verify(p => p.GetDatabase(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetByNameAsync_ReturnsNothingForAnUnknownName()
        {
            await _sut.SaveAsync(Configuration("cfg-1", "welcome", DateTime.UtcNow));

            (await _sut.GetByNameAsync("missing")).Should().BeNull();
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsNothingForAnUnknownItem()
        {
            await _sut.SaveAsync(Configuration("cfg-1", "welcome", DateTime.UtcNow));

            (await _sut.GetByIdAsync("cfg-missing")).Should().BeNull();
        }

        [Fact]
        public async Task GetConfigurationsAsync_ReturnsTheNewestFirstWithTheTotalCount()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(Configuration("cfg-1", "oldest", now.AddDays(-2)));
            await _sut.SaveAsync(Configuration("cfg-2", "newest", now));
            await _sut.SaveAsync(Configuration("cfg-3", "middle", now.AddDays(-1)));

            var response = await _sut.GetConfigurationsAsync(new GetConfigurationsRequest { Page = 0, PageSize = 10 });

            response.IsSuccess.Should().BeTrue();
            response.TotalCount.Should().Be(3);
            response.Configurations.Select(c => c.Name).Should().Equal("newest", "middle", "oldest");
        }

        [Fact]
        public async Task GetConfigurationsAsync_PagesThroughTheConfigurations()
        {
            var now = DateTime.UtcNow;
            await _sut.SaveAsync(Configuration("cfg-1", "first", now));
            await _sut.SaveAsync(Configuration("cfg-2", "second", now.AddDays(-1)));
            await _sut.SaveAsync(Configuration("cfg-3", "third", now.AddDays(-2)));

            var firstPage = await _sut.GetConfigurationsAsync(new GetConfigurationsRequest { Page = 0, PageSize = 2 });
            var secondPage = await _sut.GetConfigurationsAsync(new GetConfigurationsRequest { Page = 1, PageSize = 2 });

            firstPage.Configurations.Select(c => c.Name).Should().Equal("first", "second");
            secondPage.Configurations.Select(c => c.Name).Should().Equal("third");
            secondPage.TotalCount.Should().Be(3, "the total covers the whole collection, not the page");
        }
    }
}
