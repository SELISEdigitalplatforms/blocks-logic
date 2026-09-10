using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using MailBoxSyncService.Services;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.MailBoxSyncService
{
    public class MailRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _mockDbContextProvider = new();
        private readonly Mock<IBlocksSecret> _mockBlocksSecret = new();
        private readonly Mock<IMongoDatabase> _mockDatabase = new();
        private readonly Mock<IMongoCollection<MailBoxEntity>> _mockMailCollection = new();
        private readonly Mock<IMongoCollection<Tenant>> _mockTenantCollection = new();
        private readonly Mock<IMongoCollection<MailServerConfiguration>> _mockConfigCollection = new();
        private readonly MailRepository _repository;

        public MailRepositoryTests()
        {
            _repository = new MailRepository(_mockDbContextProvider.Object, _mockBlocksSecret.Object);
        }

        [Fact]
        public async Task InsertAsync_ShouldInsertMailEntity()
        {
            var mail = new MailBoxEntity { ItemId = "mail-123", MessageId = "message-123" };
            var tenantId = "tenant-123";

            _mockDbContextProvider.Setup(db => db.GetDatabase(tenantId)).Returns(_mockDatabase.Object);
            _mockDatabase.Setup(db => db.GetCollection<MailBoxEntity>("MailBoxEntitys", null)).Returns(_mockMailCollection.Object);

            await _repository.InsertAsync(mail, tenantId);

            _mockMailCollection.Verify(c => c.InsertOneAsync(mail, null, default), Times.Once);
        }

        [Fact]
        public async Task GetTenantsAsync_ShouldReturnActiveTenants()
        {
            var tenants = new List<Tenant>
            {
                CreateTenant("tenant-1"),
                CreateTenant("tenant-2")
            };

            _mockBlocksSecret.Setup(s => s.DatabaseConnectionString).Returns("mongodb://localhost");
            _mockBlocksSecret.Setup(s => s.RootDatabaseName).Returns("RootDB");
            _mockDbContextProvider.Setup(db => db.GetDatabase("mongodb://localhost", "RootDB", It.IsAny<bool>()))
                .Returns(_mockDatabase.Object);
            _mockDatabase.Setup(db => db.GetCollection<Tenant>("Tenants", null)).Returns(_mockTenantCollection.Object);

            var mockCursor = new Mock<IAsyncCursor<Tenant>>();
            mockCursor.Setup(c => c.Current).Returns(tenants);
            mockCursor.SetupSequence(c => c.MoveNext(default)).Returns(true).Returns(false);
            mockCursor.SetupSequence(c => c.MoveNextAsync(default)).ReturnsAsync(true).ReturnsAsync(false);

            _mockTenantCollection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<Tenant>>(),
                It.IsAny<FindOptions<Tenant, Tenant>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            var result = await _repository.GetTenantsAsync();

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetImapConfigurationsAsync_ShouldReturnInboundConfigurations()
        {
            var tenant = CreateTenant("tenant-123");
            var configs = new List<MailServerConfiguration>
            {
                new() { ItemId = "config-1", IsInbound = true },
                new() { ItemId = "config-2", IsInbound = true }
            };

            _mockDbContextProvider.Setup(db => db.GetDatabase(tenant.TenantId)).Returns(_mockDatabase.Object);
            _mockDatabase.Setup(db => db.GetCollection<MailServerConfiguration>("MailServerConfigurations", null))
                .Returns(_mockConfigCollection.Object);

            var mockCursor = new Mock<IAsyncCursor<MailServerConfiguration>>();
            mockCursor.Setup(c => c.Current).Returns(configs);
            mockCursor.SetupSequence(c => c.MoveNext(default)).Returns(true).Returns(false);
            mockCursor.SetupSequence(c => c.MoveNextAsync(default)).ReturnsAsync(true).ReturnsAsync(false);

            _mockConfigCollection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<MailServerConfiguration>>(),
                It.IsAny<FindOptions<MailServerConfiguration, MailServerConfiguration>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            var result = await _repository.GetImapConfigurationsAsync(tenant);

            result.Should().HaveCount(2);
        }

        private static Tenant CreateTenant(string tenantId)
        {
            return new Tenant
            {
                TenantId = tenantId,
                IsDisabled = false,
                DbConnectionString = "conn",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = "pass",
                    IssueDate = DateTime.UtcNow
                }
            };
        }
    }
}
