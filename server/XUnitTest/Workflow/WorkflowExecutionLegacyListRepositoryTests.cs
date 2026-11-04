using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DataAccess.InternalService;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// #402 C11: the legacy executions list (<c>GetExecutions</c> without PageSize) reads only the nine list fields,
    /// newest first, in one find. The full documents it used to read ran the API out of memory. The find is
    /// rendered to BSON; there is no Mongo server.
    /// </summary>
    public class WorkflowExecutionLegacyListRepositoryTests
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IDbContextProvider> _provider = new();
        private readonly Mock<ILogicDataAccess> _dataAccess = new();
        private readonly Mock<IMongoCollection<WorkflowExecutionEntity>> _collection = new();
        private readonly List<FindOptions<WorkflowExecutionEntity, WorkflowExecutionListRow>> _rowFinds = [];
        private readonly List<bool> _readProjections = [];
        private FilterDefinition<WorkflowExecutionEntity>? _rowFilter;
        private List<WorkflowExecutionListRow> _rows = [];

        public WorkflowExecutionLegacyListRepositoryTests()
        {
            _provider.Setup(p => p.GetCollection<WorkflowExecutionEntity>(Tenant, "WorkflowExecutions")).Returns(_collection.Object);
            GrantReads(true);

            _collection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<WorkflowExecutionEntity>>(),
                    It.IsAny<FindOptions<WorkflowExecutionEntity, WorkflowExecutionListRow>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<WorkflowExecutionEntity>, FindOptions<WorkflowExecutionEntity, WorkflowExecutionListRow>, CancellationToken>(
                    (filter, options, _) => { _rowFilter = filter; _rowFinds.Add(options); })
                .ReturnsAsync(() => Cursor(_rows));
            // The full-document read this change removes: answered so a regression fails on the assertions, not a null.
            _collection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<WorkflowExecutionEntity>>(),
                    It.IsAny<FindOptions<WorkflowExecutionEntity, WorkflowExecutionEntity>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Cursor(new List<WorkflowExecutionEntity>()));
        }

        private void GrantReads(bool granted) =>
            _dataAccess
                .Setup(d => d.ReadAsync(LogicSchemas.WorkflowExecutionEntity, It.IsAny<FilterDefinition<WorkflowExecutionEntity>>(),
                    It.IsAny<string>(), Tenant, It.IsAny<bool>()))
                .Callback<string, FilterDefinition<WorkflowExecutionEntity>, string, string?, bool>((_, _, _, _, useProjection) => _readProjections.Add(useProjection))
                // The resolver adds its row filter; the marker shows the find uses the scoped filter, not its own.
                .ReturnsAsync((string _, FilterDefinition<WorkflowExecutionEntity> filter, string _, string? _, bool _) =>
                    new ReadScope<WorkflowExecutionEntity>(granted, filter & Builders<WorkflowExecutionEntity>.Filter.Eq("RowScope", "granted"), null));

        private WorkflowExecutionRepository Create() => new(_provider.Object, _dataAccess.Object);

        private static IAsyncCursor<T> Cursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        private static RenderArgs<WorkflowExecutionEntity> Args =>
            new(BsonSerializer.LookupSerializer<WorkflowExecutionEntity>(), BsonSerializer.SerializerRegistry);

        [Fact]
        public async Task H2_OneFind_ProjectsOnlyTheNineListFields()
        {
            await Create().GetByWorkflowIdAsync("wf1", Tenant);

            var options = _rowFinds.Should().ContainSingle("H2: exactly one find").Subject;
            var projection = options.Projection.Render(Args).Document;
            projection.Names.Should().BeEquivalentTo(
                "_id", "WorkflowId", "WorkflowName", "Status", "ExecutionMode", "StartedAt", "FinishedAt", "ErrorMessage", "AttemptNumber");
            projection.Values.Should().AllSatisfy(v => v.Should().Be(new BsonInt32(1)));
            _collection.Verify(c => c.FindAsync(
                It.IsAny<FilterDefinition<WorkflowExecutionEntity>>(),
                It.IsAny<FindOptions<WorkflowExecutionEntity, WorkflowExecutionEntity>>(),
                It.IsAny<CancellationToken>()), Times.Never, "no full execution document is read");
        }

        [Fact]
        public async Task H2_SameRoundTripsAsBefore_OneScopeResolution_NoIndexCheck_NoCount_NoLimit()
        {
            await Create().GetByWorkflowIdAsync("wf1", Tenant);

            _readProjections.Should().Equal([false], "one row-scope resolution, without the resolver projection (as the paged list)");
            _rowFinds.Single().Limit.Should().BeNull("the legacy list returns every execution");
            _rowFinds.Single().Skip.Should().BeNull();
            _collection.Verify(c => c.Indexes, Times.Never);
            _collection.Verify(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<WorkflowExecutionEntity>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task H3_SortsByStartedAtThenIdDescending_AndReadsThroughTheRowScope()
        {
            await Create().GetByWorkflowIdAsync("wf1", Tenant);

            _rowFinds.Single().Sort.Render(Args).ToJson().Should().Be("{ \"StartedAt\" : -1, \"_id\" : -1 }");
            _rowFilter!.Render(Args).ToJson().Should().Be("{ \"WorkflowId\" : \"wf1\", \"RowScope\" : \"granted\" }");
        }

        [Fact]
        public async Task H1_ReturnsTheRowsTheFindReturns()
        {
            _rows = [Row("e3"), Row("e2"), Row("e1")];

            var result = await Create().GetByWorkflowIdAsync("wf1", Tenant);

            result.Select(r => r.Id).Should().Equal("e3", "e2", "e1");
        }

        [Fact]
        public async Task C12_ScopeNotGranted_ReturnsEmpty_WithoutAFind()
        {
            GrantReads(false);

            var result = await Create().GetByWorkflowIdAsync("wf1", Tenant);

            result.Should().BeEmpty();
            _rowFinds.Should().BeEmpty();
            _collection.Verify(c => c.FindAsync(
                It.IsAny<FilterDefinition<WorkflowExecutionEntity>>(),
                It.IsAny<FindOptions<WorkflowExecutionEntity, WorkflowExecutionEntity>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task C13_NoExecutions_ReturnsEmpty()
        {
            var result = await Create().GetByWorkflowIdAsync("wf-empty", Tenant);

            result.Should().BeEmpty();
            _rowFinds.Should().ContainSingle();
        }

        private static WorkflowExecutionListRow Row(string id) => new() { Id = id, WorkflowId = "wf1", WorkflowName = "name" };
    }
}
