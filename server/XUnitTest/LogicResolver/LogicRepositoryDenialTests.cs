using System.Threading;
using System.Threading.Tasks;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DataAccess.InternalService;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Workflow.DomainService.Repositories;
using Xunit;

namespace XUnitTest.LogicResolver;

/// <summary>A denied policy must stop the write, and the repository keeps its "nothing happened" answer.</summary>
public class LogicRepositoryDenialTests
{
    private readonly Mock<ILogicDataAccess> _access = new();
    private readonly Mock<IMongoCollection<WorkflowEntity>> _collection = new();
    private readonly WorkflowRepository _sut;

    public LogicRepositoryDenialTests()
    {
        var provider = new Mock<IDbContextProvider>();
        provider.Setup(p => p.GetCollection<WorkflowEntity>("t1", "Workflows")).Returns(_collection.Object);
        _sut = new WorkflowRepository(provider.Object, _access.Object);
    }

    [Fact]
    public async Task Delete_Denied_DoesNotWrite()
    {
        _access.Setup(a => a.DeleteFilterAsync(LogicSchemas.WorkflowEntity, It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<string>(), "t1"))
            .ReturnsAsync((FilterDefinition<WorkflowEntity>?)null);

        await _sut.DeleteWorkflowAsync("t1", "w1");

        _collection.Verify(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Granted_UsesTheResolverFilter()
    {
        var scoped = Builders<WorkflowEntity>.Filter.Eq(w => w.ItemId, "scoped");
        _access.Setup(a => a.DeleteFilterAsync(LogicSchemas.WorkflowEntity, It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<string>(), "t1"))
            .ReturnsAsync(scoped);

        await _sut.DeleteWorkflowAsync("t1", "w1");

        _collection.Verify(c => c.DeleteOneAsync(scoped, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Denied_DoesNotWrite_AndReportsNotFound()
    {
        _access.Setup(a => a.EditFilterAsync(LogicSchemas.WorkflowEntity, It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<string>(), "t1"))
            .ReturnsAsync((FilterDefinition<WorkflowEntity>?)null);

        var act = () => _sut.UpdateWorkflowAsync(new WorkflowEntity { ItemId = "w1", TenantId = "t1" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _collection.Verify(c => c.ReplaceOneAsync(It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<WorkflowEntity>(),
            It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Denied_DoesNotInsert()
    {
        _access.Setup(a => a.InsertAllowedAsync(LogicSchemas.WorkflowEntity, It.IsAny<string>(), "t1")).ReturnsAsync(false);

        var act = () => _sut.CreateWorkflowAsync(new WorkflowEntity { TenantId = "t1" });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _collection.Verify(c => c.InsertOneAsync(It.IsAny<WorkflowEntity>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Read_Denied_ReturnsNullWithoutQuerying()
    {
        _access.Setup(a => a.ReadAsync(LogicSchemas.WorkflowEntity, It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<string>(), "t1", It.IsAny<bool>()))
            .ReturnsAsync(new ReadScope<WorkflowEntity>(false, Builders<WorkflowEntity>.Filter.Empty, null));

        (await _sut.GetWorkflowAsync("t1", "w1")).Should().BeNull();
        (await _sut.GetWorkflowsCountAsync("t1", null, null, null, null)).Should().Be(0);
        (await _sut.GetAllWorkflowsAsync("t1", 10, 0, null, null, null, null)).Should().BeEmpty();
        _collection.Verify(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<WorkflowEntity>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
