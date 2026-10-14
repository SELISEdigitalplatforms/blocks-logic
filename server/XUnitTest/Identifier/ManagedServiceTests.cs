using Blocks.Genesis;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.Shared;
using DomainService.Shared.Entities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class ServiceManagementTests : IDisposable
    {
        private readonly Mock<IServiceManagementRepository> _repository = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<ILogger<ServiceManagement>> _logger = new();

        public ServiceManagementTests()
        {
            TestBlocksContext.Set();
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ServiceManagement CreateService() => new(
            _logger.Object,
            _repository.Object,
            _blocksSecret.Object,
            _cacheClient.Object,
            _tenants.Object,
            _configuration.Object);

        private static Tenant Tenant(string salt) => new()
        {
            TenantId = "tenant-123",
            TenantSalt = salt,
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow }
        };

        [Fact]
        public async Task GetAllServicesAsync_DecryptsConnectionStringsWithTheTenantSalt()
        {
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("salty"));

            var stored = new BlocksManagedService
            {
                ItemId = "svc-1",
                ServiceId = "SB-1",
                Name = "logs-collector",
                ServiceType = "frontend",
                ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://real-connection", "salty")
            };
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ReturnsAsync((new List<BlocksManagedService> { stored }.AsQueryable(), 1));

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.TotalCount.Should().Be(1);
            result.Data!.Single()!.ServiceBusConnectionString.Should().Be("amqp://real-connection");
            result.Data!.Single()!.ServiceType.Should().Be("frontend");
        }

        [Fact]
        public async Task GetAllServicesAsync_UnknownTenantAndMissingServiceType_UsesDefaults()
        {
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);

            var stored = new BlocksManagedService
            {
                ItemId = "svc-1",
                ServiceId = "SB-1",
                Name = "logs-collector",
                ServiceType = null!,
                ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://real-connection", "LMT")
            };
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ReturnsAsync((new List<BlocksManagedService> { stored }.AsQueryable(), 1));

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Single()!.ServiceBusConnectionString.Should().Be("amqp://real-connection");
            result.Data!.Single()!.ServiceType.Should().Be("backend");
        }

        // AES with a wrong key can give valid padding by chance (~1/256), so pick a ciphertext the wrong keys cannot open.
        private static string CipherNoOtherKeyOpens(string plain, string rightSalt, params string[] wrongSalts)
        {
            while (true)
            {
                var cipher = EncryptionHelper.Encrypt(plain, rightSalt);
                if (wrongSalts.All(w => !EncryptionHelper.TryDecrypt(cipher, w, out _)))
                {
                    return cipher;
                }
            }
        }

        [Fact]
        public async Task GetAllServicesAsync_ImpersonatedCaller_RowEncryptedWithTheProjectSalt_Decrypts_NoErrorLogged()
        {
            TestBlocksContext.SetImpersonated("tenant-123", "root-tenant");
            _configuration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("project-salt"));
            _tenants.Setup(t => t.GetTenantByID("root-tenant")).Returns(Tenant("console-salt"));
            var rows = new List<BlocksManagedService>
            {
                new() { ItemId = "1", ServiceId = "SB-1", ServiceBusConnectionString = CipherNoOtherKeyOpens("amqp://project", "project-salt", "console-salt", "LMT") },
                new() { ItemId = "2", ServiceId = "SB-2", ServiceBusConnectionString = CipherNoOtherKeyOpens("amqp://console", "console-salt", "project-salt", "LMT") },
            };
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ReturnsAsync((rows.AsQueryable(), 2));

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Select(d => d!.ServiceBusConnectionString).Should().Equal("amqp://project", "amqp://console");
            _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
        }

        [Fact]
        public async Task GetAllServicesAsync_RowsWithTwoSalts_EachDecryptsWithTheTenantSaltElseTheRootTenantSalt_NoErrorLogged()
        {
            _configuration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("project-salt"));
            _tenants.Setup(t => t.GetTenantByID("root-tenant")).Returns(Tenant("console-salt"));
            var rows = new List<BlocksManagedService>
            {
                new() { ItemId = "1", ServiceId = "SB-1", ServiceBusConnectionString = CipherNoOtherKeyOpens("amqp://project", "project-salt", "console-salt", "LMT") },
                new() { ItemId = "2", ServiceId = "SB-2", ServiceBusConnectionString = CipherNoOtherKeyOpens("amqp://console", "console-salt", "project-salt", "LMT") },
            };
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ReturnsAsync((rows.AsQueryable(), 2));

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Select(d => d!.ServiceBusConnectionString).Should().Equal("amqp://project", "amqp://console");
            _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
        }

        [Fact]
        public async Task GetAllServicesAsync_RowNoKeyOpens_ReturnsEmptyAndLogsOneError()
        {
            _configuration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("project-salt"));
            _tenants.Setup(t => t.GetTenantByID("root-tenant")).Returns(Tenant("console-salt"));
            var rows = new List<BlocksManagedService>
            {
                new() { ItemId = "1", ServiceId = "SB-1", ServiceBusConnectionString = CipherNoOtherKeyOpens("amqp://x", "some-other-salt", "project-salt", "console-salt", "LMT") },
            };
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ReturnsAsync((rows.AsQueryable(), 1));

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Single()!.ServiceBusConnectionString.Should().BeEmpty();
            _logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public async Task GetAllServicesAsync_RepositoryThrows_Rethrows()
        {
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            var act = async () => await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    public class ServiceManagementRepositoryTests : IDisposable
    {
        private const string CollectionName = "BlocksManagedServices";

        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();
        private readonly Mock<IMongoDatabase> _clientDb = new();
        private readonly Mock<IMongoCollection<BlocksManagedService>> _collection = new();
        private readonly Mock<IMongoDatabase> _rootDb = new();
        private readonly Mock<IMongoCollection<BlocksManagedService>> _rootCollection = new();

        public ServiceManagementRepositoryTests()
        {
            TestBlocksContext.Set();
            _dbContextProvider.Setup(p => p.GetDatabase("tenant-123")).Returns(_clientDb.Object);
            _clientDb.Setup(d => d.GetCollection<BlocksManagedService>(CollectionName, null)).Returns(_collection.Object);
            // Default: the tenant DB still has BlocksManagedServices (today's behavior).
            TenantHasCollection(true);

            _blocksSecret.Setup(s => s.DatabaseConnectionString).Returns("mongodb://root");
            _blocksSecret.Setup(s => s.RootDatabaseName).Returns("BlocksRootDb");
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://root", "BlocksRootDb", It.IsAny<bool>())).Returns(_rootDb.Object);
            _rootDb.Setup(d => d.GetCollection<BlocksManagedService>(CollectionName, null)).Returns(_rootCollection.Object);
        }

        private void TenantHasCollection(bool exists)
        {
            var names = exists ? new List<string> { CollectionName } : new List<string>();
            var cursor = new Mock<IAsyncCursor<string>>();
            cursor.Setup(c => c.Current).Returns(names);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(names.Count > 0).ReturnsAsync(false);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(names.Count > 0).Returns(false);
            _clientDb.Setup(d => d.ListCollectionNamesAsync(It.IsAny<ListCollectionNamesOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
        }

        private void SetupRootFind(List<BlocksManagedService> services, long count)
        {
            var cursor = new Mock<IAsyncCursor<BlocksManagedService>>();
            cursor.Setup(c => c.Current).Returns(services);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(services.Count > 0).ReturnsAsync(false);
            _rootCollection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BlocksManagedService>>(),
                    It.IsAny<FindOptions<BlocksManagedService, BlocksManagedService>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            _rootCollection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BlocksManagedService>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(count);
        }

        [Fact]
        public async Task GetAllServicesAsync_TenantCollectionPresent_ReadsTheTenantDb()
        {
            SetupFind([new BlocksManagedService { ItemId = "t", Name = "tenant-row" }], 1);

            var (services, _) = await CreateRepository().GetAllServicesAsync(new GetAllServiceRequest { PageSize = 10 });

            services.Single().Name.Should().Be("tenant-row");
            _dbContextProvider.Verify(p => p.GetDatabase("mongodb://root", "BlocksRootDb", It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task GetAllServicesAsync_TenantCollectionGone_ReadsRoot()
        {
            TenantHasCollection(false);
            SetupRootFind([new BlocksManagedService { ItemId = "r", Name = "root-row" }], 1);

            var (services, count) = await CreateRepository().GetAllServicesAsync(new GetAllServiceRequest { PageSize = 10 });

            services.Single().Name.Should().Be("root-row");
            count.Should().Be(1);
            // Nothing was created in the tenant DB.
            _clientDb.Verify(d => d.GetCollection<BlocksManagedService>(CollectionName, null), Times.Never);
        }

        [Fact]
        public async Task GetAllServicesAsync_Impersonated_ReadsRoot_WithoutTheTenantCheck()
        {
            TestBlocksContext.SetImpersonated("tenant-123", "root");
            SetupRootFind([new BlocksManagedService { ItemId = "r", Name = "root-row" }], 1);

            var (services, _) = await CreateRepository().GetAllServicesAsync(new GetAllServiceRequest { PageSize = 10 });

            services.Single().Name.Should().Be("root-row");
            _clientDb.Verify(d => d.ListCollectionNamesAsync(It.IsAny<ListCollectionNamesOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ServiceManagementRepository CreateRepository() => new(_dbContextProvider.Object, _blocksSecret.Object);

        private void SetupFind(List<BlocksManagedService> services, long count)
        {
            var cursor = new Mock<IAsyncCursor<BlocksManagedService>>();
            cursor.Setup(c => c.Current).Returns(services);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(services.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(services.Count > 0).ReturnsAsync(false);

            _collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BlocksManagedService>>(),
                    It.IsAny<FindOptions<BlocksManagedService, BlocksManagedService>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);

            _collection.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BlocksManagedService>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(count);
        }

        [Fact]
        public async Task GetAllServicesAsync_NoFilter_ReturnsAllServicesForTheTenant()
        {
            SetupFind([new BlocksManagedService { ItemId = "svc-1", Name = "logs", ServiceId = "SB-1" }], 1);

            var (services, count) = await CreateRepository().GetAllServicesAsync(new GetAllServiceRequest { Page = 0, PageSize = 10 });

            services.Should().HaveCount(1);
            count.Should().Be(1);
        }

        [Fact]
        public async Task GetAllServicesAsync_WithNameAndIdFilter_StillQueriesTheCollection()
        {
            SetupFind([], 0);

            var (services, count) = await CreateRepository().GetAllServicesAsync(new GetAllServiceRequest
            {
                Page = 1,
                PageSize = 5,
                Filter = new GetAllServiceFilter { ServiceName = "logs", ServiceId = "SB-1" }
            });

            services.Should().BeEmpty();
            count.Should().Be(0);
            _dbContextProvider.Verify(p => p.GetDatabase("tenant-123"), Times.Once);
        }
    }
}
