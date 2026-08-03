using Blocks.Genesis;
using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.Shared;
using DomainService.Shared.Entities;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
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
        private readonly Mock<IValidator<RegisterServiceRequest>> _validator = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<ILogger<ServiceManagement>> _logger = new();

        public ServiceManagementTests()
        {
            TestBlocksContext.Set();
            // A RabbitMq connection string keeps the constructor away from the Azure Service Bus clients.
            _blocksSecret.SetupGet(s => s.LmtMessageConnectionString).Returns("amqp://guest:guest@localhost:5672");
            _blocksSecret.SetupGet(s => s.LogConnectionString).Returns("mongodb://localhost:27017");
            _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterServiceRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ServiceManagement CreateService() => new(
            _repository.Object,
            _validator.Object,
            _blocksSecret.Object,
            _cacheClient.Object,
            _tenants.Object,
            _configuration.Object,
            _logger.Object);

        private static Tenant Tenant(string salt) => new()
        {
            TenantId = "tenant-123",
            TenantSalt = salt,
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow }
        };

        [Fact]
        public void Map_BuildsAManagedServiceFromTheRequestAndContext()
        {
            var request = new RegisterServiceRequest
            {
                ServiceName = "logs-collector",
                Description = "collects logs",
                ServiceType = "backend",
                Tags = ["logs"],
                Metadata = new Dictionary<string, object> { { "team", "platform" } }
            };

            var service = CreateService().Map(request);

            service.Name.Should().Be("logs-collector");
            service.Description.Should().Be("collects logs");
            service.ServiceType.Should().Be("backend");
            service.Tags.Should().BeEquivalentTo(["logs"]);
            service.Metadata.Should().ContainKey("team");
            service.TenantId.Should().Be("tenant-123");
            service.CreatedBy.Should().Be("user-123");
            service.LastUpdatedBy.Should().Be("user-123");
            service.ItemId.Should().NotBeNullOrWhiteSpace();
            service.ServiceId.Should().StartWith("SB-");
        }

        [Fact]
        public void Map_WithoutDescription_UsesEmptyString()
        {
            var service = CreateService().Map(new RegisterServiceRequest { ServiceName = "svc", ServiceType = "frontend" });

            service.Description.Should().BeEmpty();
        }

        [Fact]
        public async Task RegisterServiceAsync_InvalidRequest_ReturnsValidationErrors()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterServiceRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("ServiceName", "ServiceName is required.")]));

            var result = await CreateService().RegisterServiceAsync(new RegisterServiceRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors!["ServiceName"].Should().Be("ServiceName is required.");
            _repository.Verify(r => r.SaveAsync(It.IsAny<BlocksManagedService>()), Times.Never);
        }

        [Fact]
        public async Task RegisterServiceAsync_ValidationFailureWithoutPropertyName_UsesGenericKey()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterServiceRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure(string.Empty, "something is wrong")]));

            var result = await CreateService().RegisterServiceAsync(new RegisterServiceRequest());

            result.Errors!["validation_error"].Should().Be("something is wrong");
        }

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
        private readonly Mock<IMongoCollection<BlocksManagedService>> _collection = new();

        public ServiceManagementRepositoryTests()
        {
            TestBlocksContext.Set();
            _dbContextProvider.Setup(p => p.GetCollection<BlocksManagedService>(CollectionName)).Returns(_collection.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ServiceManagementRepository CreateRepository() => new(_dbContextProvider.Object);

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
            _dbContextProvider.Verify(p => p.GetCollection<BlocksManagedService>(CollectionName), Times.Once);
        }

        [Fact]
        public async Task SaveAsync_InsertsTheService()
        {
            var service = new BlocksManagedService { ItemId = "svc-1", ServiceId = "SB-1", Name = "logs" };

            await CreateRepository().SaveAsync(service);

            _collection.Verify(c => c.InsertOneAsync(service, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
