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
        private readonly Mock<Blocks.Secrets.ISecretService> _secrets = new(MockBehavior.Strict);

        public ServiceManagementTests()
        {
            TestBlocksContext.Set();
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ServiceManagement CreateService(ILogger<ServiceManagement>? logger = null) => new(
            logger ?? _logger.Object,
            _repository.Object,
            _blocksSecret.Object,
            _cacheClient.Object,
            _tenants.Object,
            _configuration.Object,
            new ServiceBusConnectionStringResolver(MailSecretsTestDoubles.ScopeFactory(_secrets)));

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

        // ============ #371: ServiceBusConnectionStringReference (H8, H9, C8, Q2, Q4) ============

        private const string ServiceBusValue = "Endpoint=sb://x/;SharedAccessKeyName=k;SharedAccessKey=v";

        private void GivenRows(params BlocksManagedService[] rows) =>
            _repository.Setup(r => r.GetAllServicesAsync(It.IsAny<GetAllServiceRequest>()))
                .ReturnsAsync((rows.AsQueryable(), rows.Length));

        private int SecretReads => _secrets.Invocations.Count(i => i.Method.Name == nameof(Blocks.Secrets.ISecretService.GetValueAsync));

        [Fact]
        public async Task GetAllServicesAsync_ReferenceRowAndLegacyRow_ResolvesEachItsOwnWay()
        {
            // H8, H9 (E5): R1 from Blocks Secrets with the reference nulled; R2 by today's ladder;
            // exactly one secret read, in the row's tenant.
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("salty"));
            var seen = new List<BlocksContext?>();
            _secrets.Setup(s => s.GetValueAsync("s-7", It.IsAny<CancellationToken>()))
                    .Callback(() => seen.Add(BlocksContext.GetContext()))
                    .ReturnsAsync(ServiceBusValue);
            GivenRows(
                new BlocksManagedService { ItemId = "1", ServiceId = "R1", TenantId = "tenant-123", ServiceBusConnectionString = "", ServiceBusConnectionStringReference = "s-7" },
                new BlocksManagedService { ItemId = "2", ServiceId = "R2", TenantId = "tenant-123", ServiceType = "frontend", ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://legacy", "salty") });
            var before = BlocksContext.GetContext();

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            var rows = result.Data!.ToList();
            rows.Select(r => r!.ServiceBusConnectionString).Should().Equal(ServiceBusValue, "amqp://legacy");
            rows.Should().OnlyContain(r => r!.ServiceBusConnectionStringReference == null);
            rows.Select(r => r!.ServiceType).Should().Equal("backend", "frontend");
            result.TotalCount.Should().Be(2);
            SecretReads.Should().Be(1);
            seen.Single()!.TenantId.Should().Be("tenant-123");
            seen.Single()!.IsAuthenticated.Should().BeTrue();
            BlocksContext.GetContext().Should().BeSameAs(before);
        }

        [Fact]
        public async Task GetAllServicesAsync_ReferenceWinsOverANonEmptyCiphertext()
        {
            // The reference rule: the legacy field is not used once a reference is set.
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("salty"));
            _secrets.Setup(s => s.GetValueAsync("s-7", It.IsAny<CancellationToken>())).ReturnsAsync(ServiceBusValue);
            GivenRows(new BlocksManagedService
            {
                ItemId = "1", ServiceId = "R1", TenantId = "tenant-123",
                ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://stale", "salty"),
                ServiceBusConnectionStringReference = "s-7"
            });

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Single()!.ServiceBusConnectionString.Should().Be(ServiceBusValue);
        }

        [Theory]
        [InlineData(nameof(Blocks.Secrets.SecretNotFoundException))]
        [InlineData(nameof(Blocks.Secrets.SecretStateException))]
        [InlineData(nameof(Blocks.Secrets.SecretAccessDeniedException))]
        [InlineData(nameof(Blocks.Secrets.SecretVaultException))]
        [InlineData(nameof(Blocks.Secrets.SecretValidationException))]
        [InlineData(nameof(InvalidOperationException))]
        public async Task GetAllServicesAsync_OneRowsSecretReadFails_BlanksThatRowOnly_AndLogsItsServiceId(string failure)
        {
            // C8 (E6), Q4, C4: R3 is "", the others are returned, one error names the service id and
            // the exception type, and nothing logged carries the reference id.
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("salty"));
            _secrets.Setup(s => s.GetValueAsync("s-7", It.IsAny<CancellationToken>())).ReturnsAsync(ServiceBusValue);
            _secrets.Setup(s => s.GetValueAsync("s-8", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(MailSecretsTestDoubles.SecretFailure(failure, "s-8"));
            GivenRows(
                new BlocksManagedService { ItemId = "1", ServiceId = "R1", TenantId = "tenant-123", ServiceBusConnectionStringReference = "s-7" },
                new BlocksManagedService { ItemId = "3", ServiceId = "R3", TenantId = "tenant-123", ServiceBusConnectionStringReference = "s-8" },
                new BlocksManagedService { ItemId = "2", ServiceId = "R2", TenantId = "tenant-123", ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://legacy", "salty") });
            var logger = new CapturingLogger<ServiceManagement>();

            var result = await CreateService(logger).GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Select(r => r!.ServiceBusConnectionString).Should().Equal(ServiceBusValue, "", "amqp://legacy");
            result.Data!.Should().OnlyContain(r => r!.ServiceBusConnectionStringReference == null);
            logger.Lines.Should().ContainSingle()
                .Which.Should().Contain("R3").And.Contain(failure);
            logger.All.Should().NotContain("s-8").And.NotContain("s-7").And.NotContain(ServiceBusValue);
        }

        [Fact]
        public async Task GetAllServicesAsync_ImpersonatedCaller_ReadsInTheProjectTenant_AndRestoresTheImpersonatedContext()
        {
            // Q2: under impersonation the secret is read in the project (row/context) tenant, where
            // blocks-os writes it, not in the console tenant.
            TestBlocksContext.SetImpersonated("tenant-123", "root-tenant");
            _configuration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(Tenant("salty"));
            var seen = new List<BlocksContext?>();
            _secrets.Setup(s => s.GetValueAsync("s-7", It.IsAny<CancellationToken>()))
                    .Callback(() => seen.Add(BlocksContext.GetContext()))
                    .ReturnsAsync(ServiceBusValue);
            GivenRows(new BlocksManagedService { ItemId = "1", ServiceId = "R1", TenantId = "tenant-123", ServiceBusConnectionStringReference = "s-7" });
            var before = BlocksContext.GetContext();

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Single()!.ServiceBusConnectionString.Should().Be(ServiceBusValue);
            seen.Single()!.TenantId.Should().Be("tenant-123");
            BlocksContext.GetContext().Should().BeSameAs(before);
            BlocksContext.GetContext()!.Impersonated.Should().BeTrue();
        }

        [Fact]
        public async Task GetAllServicesAsync_ReferenceRowWithoutATenant_ReadsInTheContextTenant()
        {
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("salty"));
            var seen = new List<BlocksContext?>();
            _secrets.Setup(s => s.GetValueAsync("s-7", It.IsAny<CancellationToken>()))
                    .Callback(() => seen.Add(BlocksContext.GetContext()))
                    .ReturnsAsync(ServiceBusValue);
            GivenRows(new BlocksManagedService { ItemId = "1", ServiceId = "R1", TenantId = "", ServiceBusConnectionStringReference = "s-7" });

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Single()!.ServiceBusConnectionString.Should().Be(ServiceBusValue);
            seen.Single()!.TenantId.Should().Be("tenant-123");
        }

        [Fact]
        public async Task GetAllServicesAsync_ReferenceRowWithNoTenantAnywhere_IsBlankedWithoutARead()
        {
            TestBlocksContext.Clear();
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            GivenRows(new BlocksManagedService { ItemId = "1", ServiceId = "R1", TenantId = "", ServiceBusConnectionStringReference = "s-7" });
            var logger = new CapturingLogger<ServiceManagement>();

            var result = await CreateService(logger).GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Single()!.ServiceBusConnectionString.Should().BeEmpty();
            SecretReads.Should().Be(0);
            logger.All.Should().Contain("R1").And.NotContain("s-7");
        }

        [Fact]
        public async Task GetAllServicesAsync_LegacyRowsOnly_MakeNoSecretRead()
        {
            // H9, C10: 0 reads for legacy rows (the strict mock fails any).
            _tenants.Setup(t => t.GetTenantByID("tenant-123")).Returns(Tenant("salty"));
            GivenRows(
                new BlocksManagedService { ItemId = "1", ServiceId = "R1", ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://a", "salty") },
                new BlocksManagedService { ItemId = "2", ServiceId = "R2", ServiceBusConnectionString = EncryptionHelper.Encrypt("amqp://b", "salty"), ServiceBusConnectionStringReference = "" });

            var result = await CreateService().GetAllServicesAsync(new GetAllServiceRequest());

            result.Data!.Select(r => r!.ServiceBusConnectionString).Should().Equal("amqp://a", "amqp://b");
            result.Data!.Should().OnlyContain(r => r!.ServiceBusConnectionStringReference == null);
            _secrets.Invocations.Should().BeEmpty();
        }

        [Fact]
        public void BlocksManagedService_DeserializesWithAndWithoutTheReference_UnderTheExactElementName()
        {
            var legacy = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BlocksManagedService>(
                MongoDB.Bson.BsonDocument.Parse("""{ "_id": "1", "ServiceId": "R2", "ServiceBusConnectionString": "cipher" }"""));
            var referenced = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BlocksManagedService>(
                MongoDB.Bson.BsonDocument.Parse("""{ "_id": "2", "ServiceId": "R1", "ServiceBusConnectionString": "", "ServiceBusConnectionStringReference": "s-7" }"""));

            legacy.ServiceBusConnectionStringReference.Should().BeNull();
            referenced.ServiceBusConnectionStringReference.Should().Be("s-7");
            MongoDB.Bson.Serialization.BsonClassMap.LookupClassMap(typeof(BlocksManagedService))
                .GetMemberMap(nameof(BlocksManagedService.ServiceBusConnectionStringReference))
                .ElementName.Should().Be("ServiceBusConnectionStringReference");
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
