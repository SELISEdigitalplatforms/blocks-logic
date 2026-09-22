using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Repositories;

namespace XUnitTest.Scheduler;

public class ScheduleRepositoryRoutingTests
{
    private static Mock<IMongoCollection<T>> Collection<T>(params T[] items)
    {
        var collection = new Mock<IMongoCollection<T>>();
        collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<FindOptions<T, T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var cursor = new Mock<IAsyncCursor<T>>();
                cursor.SetupGet(c => c.Current).Returns(items);
                cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
                return cursor.Object;
            });
        return collection;
    }

    [Fact]
    public async Task Discovery_UsesStoredConnections_ForSameNamedDatabases_AndIsolatesFailedTenants()
    {
        var provider = new Mock<IDbContextProvider>(MockBehavior.Strict);
        var secret = new BlocksSecret { DatabaseConnectionString = "main", RootDatabaseName = "central-root" };
        var logger = new Mock<ILogger<ScheduleRepository>>();
        var tenants = Collection(
            new Tenant { JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "test", IssueDate = DateTime.UtcNow }, TenantId = "dev", DbConnectionString = "dev-connection", DBName = "same-name" },
            new Tenant { JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "test", IssueDate = DateTime.UtcNow }, TenantId = "stg", DbConnectionString = "other-connection", DBName = "same-name" },
            new Tenant { JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "test", IssueDate = DateTime.UtcNow }, TenantId = "offline", DbConnectionString = "offline-connection", DBName = "same-name" },
            new Tenant { JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "test", IssueDate = DateTime.UtcNow }, TenantId = "disabled", IsDisabled = true, DbConnectionString = "disabled-connection", DBName = "same-name" },
            new Tenant { JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "test", IssueDate = DateTime.UtcNow }, TenantId = "missing-connection", DbConnectionString = "", DBName = "same-name" });
        var root = new Mock<IMongoDatabase>();
        root.Setup(d => d.GetCollection<Tenant>("Tenants", null)).Returns(tenants.Object);
        provider.Setup(p => p.GetDatabase("main", "central-root", false)).Returns(root.Object);
        foreach (var connection in new[] { "dev-connection", "other-connection" })
        {
            var database = new Mock<IMongoDatabase>();
            database.Setup(d => d.GetCollection<Schedule>("Schedules", null))
                .Returns(Collection(new Schedule { ItemId = "same-schedule-id", Name = connection, IsActive = true }).Object);
            provider.Setup(p => p.GetDatabase(connection, "same-name", false)).Returns(database.Object);
        }
        provider.Setup(p => p.GetDatabase("offline-connection", "same-name", false)).Throws(new TimeoutException());

        var results = await new ScheduleRepository(logger.Object, provider.Object, secret).GetSchedulesFromAllTenantsAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("dev-connection", Assert.Single(results.Single(r => r.TenantId == "dev").Schedules).Name);
        Assert.Equal("other-connection", Assert.Single(results.Single(r => r.TenantId == "stg").Schedules).Name);
        provider.Verify(p => p.GetDatabase("disabled-connection", "same-name", false), Times.Never);
        provider.Verify(p => p.GetDatabase("main", "same-name", false), Times.Never);
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("offline")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("missing-connection")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task RootRegistryFailure_Propagates_InsteadOfReportingAnEmptyScan()
    {
        var provider = new Mock<IDbContextProvider>();
        var secret = new BlocksSecret { DatabaseConnectionString = "main", RootDatabaseName = "root" };
        provider.Setup(p => p.GetDatabase("main", "root", false)).Throws(new TimeoutException());
        await Assert.ThrowsAsync<TimeoutException>(() => new ScheduleRepository(
            Mock.Of<ILogger<ScheduleRepository>>(), provider.Object, secret).GetSchedulesFromAllTenantsAsync());
    }
}
