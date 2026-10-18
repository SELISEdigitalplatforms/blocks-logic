using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blocks.Genesis;
using Blocks.Genesis.DataAccess;
using Blocks.Genesis.DataAccess.Rules;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using DataAccess.InternalService;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace XUnitTest.LogicResolver;

/// <summary>
/// Part B for Logic: the wrapper runs a schema's validation rules on writes and its defaults on inserts, only
/// for schemas the Genesis allow-list opens, only with a signed-in user of the same tenant.
/// </summary>
public class LogicWriteRulesTests : IDisposable
{
    private readonly Mock<IDataAccessResolver> _resolver = new();
    private readonly LogicDataAccess _sut;
    private BsonDocument? _sent;

    public LogicWriteRulesTests()
    {
        BlocksContext.IsTestMode = true;
        SetContext(authenticated: true);
        _sut = new LogicDataAccess(_resolver.Object, NullLogger<LogicDataAccess>.Instance);
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null);
        BlocksContext.IsTestMode = false;
    }

    private static void SetContext(bool authenticated) =>
        BlocksContext.SetContext(BlocksContext.Create(
            "t1", new[] { "role" }, "user-1", authenticated, "uri", "org",
            DateTime.UtcNow.AddHours(1), "e@x.com", new[] { "perm" }, "octo",
            "phone", "display", "oauth", "t1"));

    /// <summary>The resolver answers with <paramref name="change"/> applied to the document it was sent.</summary>
    private void Resolves(Action<BsonDocument>? change = null, DataValidationResult? validation = null) =>
        _resolver.Setup(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, BsonDocument document, bool _, BsonDocument? _, CancellationToken _) =>
            {
                _sent = document;
                change?.Invoke(document);
                return new ResolvedWrite(document, validation ?? new DataValidationResult(), DefaultsApplied: change is not null);
            });

    private static DataValidationResult Failed(string field, string message)
    {
        var result = new DataValidationResult();
        result.AddError(field, message, "MaxLength");
        return result;
    }

    [Fact]
    public async Task Insert_DefaultsLandInTheCallersOwnInstance()
    {
        Resolves(document => document["Description"] = "Runs every weekday");
        var schedule = new Schedule { ItemId = "s1", Name = "Daily sync" };

        var returned = await _sut.PrepareWriteAsync(LogicSchemas.Schedule, schedule, isInsert: true, "test");

        returned.Should().BeSameAs(schedule);
        schedule.Description.Should().Be("Runs every weekday");
        schedule.Name.Should().Be("Daily sync");
        _resolver.Verify(r => r.ResolveWriteAsync("Schedule", It.IsAny<BsonDocument>(), true, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ABrokenRule_Throws_WithThePropertyAndMessage()
    {
        Resolves(validation: Failed("Name", "Name must be at most 5 characters"));

        var act = () => _sut.PrepareWriteAsync(LogicSchemas.WorkflowEntity, new WorkflowEntity { ItemId = "w1", TenantId = "t1", Name = "Onboarding" }, isInsert: false, "test");

        (await act.Should().ThrowAsync<DataValidationException>()).Which.Result.Errors.Should().ContainSingle()
            .Which.FieldName.Should().Be("Name");
    }

    [Theory]
    [InlineData(LogicSchemas.FunctionRunEntity)]
    [InlineData(LogicSchemas.WorkflowExecutionEntity)]
    [InlineData(LogicSchemas.MailBoxEntity)]
    public async Task SchemasTheAllowListKeepsClosed_NeverReachTheResolver(string schema)
    {
        await _sut.PrepareWriteAsync(schema, new Schedule(), isInsert: true, "test");
        await _sut.CheckUpdateAsync(schema, Builders<Schedule>.Update.Set(s => s.Name, "x"), "test");

        _resolver.Verify(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutASignedInUser_NoRulesRun()
    {
        SetContext(authenticated: false);

        await _sut.PrepareWriteAsync(LogicSchemas.Schedule, new Schedule(), isInsert: true, "test");

        _resolver.Verify(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APartialUpdate_SendsOnlyItsSetFields_AsNestedProperties()
    {
        Resolves();
        var update = Builders<Schedule>.Update
            .Set("Read.Roles", new BsonArray { "admin" })
            .Set(s => s.Name, "Ann")
            .Set("Items.0.Name", "skipped")
            .Inc("RunCount", 1);

        await _sut.CheckUpdateAsync(LogicSchemas.Schedule, update, "test");

        _sent.Should().BeEquivalentTo(new BsonDocument
        {
            { "Read", new BsonDocument("Roles", new BsonArray { "admin" }) },
            { "Name", "Ann" },
        });
        _resolver.Verify(r => r.ResolveWriteAsync("Schedule", It.IsAny<BsonDocument>(), false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUpdateWithNothingSet_IsNotChecked()
    {
        await _sut.CheckUpdateAsync(LogicSchemas.Schedule, Builders<Schedule>.Update.Inc("RunCount", 1), "test");

        _resolver.Verify(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AMissingSchema_IsAllowed()
    {
        _resolver.Setup(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("no schema"));
        var workflow = new WorkflowEntity { ItemId = "w1", TenantId = "t1" };

        (await _sut.PrepareWriteAsync(LogicSchemas.WorkflowEntity, workflow, isInsert: true, "test")).Should().BeSameAs(workflow);
    }

    [Fact]
    public async Task TheWorkflowRepository_WritesNothing_WhenARuleFails()
    {
        var access = new Mock<ILogicDataAccess>();
        access.Setup(a => a.InsertAllowedAsync(LogicSchemas.WorkflowEntity, It.IsAny<string>(), "t1")).ReturnsAsync(true);
        access.Setup(a => a.PrepareWriteAsync(LogicSchemas.WorkflowEntity, It.IsAny<WorkflowEntity>(), true, It.IsAny<string>(), "t1"))
            .ThrowsAsync(new DataValidationException(Failed("Name", "Name is required")));
        // Strict: an InsertOneAsync on it would throw a MockException instead of the validation error.
        var collection = new Mock<IMongoCollection<WorkflowEntity>>(MockBehavior.Strict);
        var provider = new Mock<IDbContextProvider>();
        provider.Setup(p => p.GetCollection<WorkflowEntity>("t1", "Workflows")).Returns(collection.Object);
        var repository = new global::Workflow.DomainService.Repositories.WorkflowRepository(provider.Object, access.Object);

        var act = () => repository.CreateWorkflowAsync(new WorkflowEntity { ItemId = "w1", TenantId = "t1" });

        await act.Should().ThrowAsync<DataValidationException>();
    }
}
