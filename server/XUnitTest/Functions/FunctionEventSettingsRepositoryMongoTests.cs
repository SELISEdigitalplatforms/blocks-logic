using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The Actions switch of a function, against a real MongoDB: the subscriber lookup skips a version whose switch is off,
    /// a row written before the switch existed counts as on, and deploy copies the switch. Needs a local MongoDB:
    /// <c>BLOCKS_FUNCTIONS_TEST_MONGO</c> (default <c>mongodb://localhost:27017</c>); each run drops its own database.
    /// </summary>
    public sealed class FunctionEventSettingsRepositoryMongoTests : IDisposable
    {
        private const string Tenant = "tenant-actions-switch";
        private const string Action = "iam.user.signed_up";

        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_fn_actions_" + Guid.NewGuid().ToString("N");
        private readonly IMongoCollection<FunctionEventSettingsEntity> _collection;
        private readonly FunctionEventSettingsRepository _repository;

        public FunctionEventSettingsRepositoryMongoTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            _collection = _client.GetDatabase(_databaseName)
                .GetCollection<FunctionEventSettingsEntity>(FunctionsConstants.FunctionEventSettingsCollection);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<FunctionEventSettingsEntity>(It.IsAny<string>(), It.IsAny<string>())).Returns(_collection);
            _repository = new FunctionEventSettingsRepository(provider.Object);
        }

        public void Dispose() => _client.DropDatabase(_databaseName);

        private async Task DeployAsync(string functionId, bool actionsEnabled)
        {
            await _repository.SaveDraftAsync(Tenant, functionId, [Action], actionsEnabled, "user-1");
            await _repository.CopyDraftToVersionAsync(Tenant, functionId, functionId + "-v1");
        }

        [Fact]
        public async Task The_subscriber_lookup_skips_a_version_whose_Actions_switch_is_off()
        {
            await DeployAsync("fn-on", actionsEnabled: true);
            await DeployAsync("fn-off", actionsEnabled: false);

            var found = await _repository.FindSubscriptionsAsync(Tenant, Action, null, 50);

            found.Should().Equal(new FunctionEventSubscription("fn-on", "fn-on-v1"));
        }

        [Fact]
        public async Task A_row_written_before_the_switch_existed_counts_as_on()
        {
            await _client.GetDatabase(_databaseName).GetCollection<BsonDocument>(FunctionsConstants.FunctionEventSettingsCollection)
                .InsertOneAsync(new BsonDocument
                {
                    { "_id", FunctionEventSettingsEntity.IdFor("fn-old", "fn-old-v1") },
                    { "FunctionId", "fn-old" },
                    { "VersionId", "fn-old-v1" },
                    { "TriggerEvents", new BsonArray { Action } },
                });

            var found = await _repository.FindSubscriptionsAsync(Tenant, Action, null, 50);
            var row = await _repository.GetAsync(Tenant, "fn-old", "fn-old-v1");

            found.Should().ContainSingle().Which.FunctionId.Should().Be("fn-old");
            row!.ActionsEnabled.Should().BeNull();
            row.ActiveTrigger.Should().Be(Action);
        }

        [Fact]
        public async Task GetMany_reads_the_draft_and_version_rows_asked_for_in_one_go()
        {
            await DeployAsync("fn-1", actionsEnabled: true);
            await _repository.SaveDraftAsync(Tenant, "fn-1", [Action], actionsEnabled: false, "user-1");
            await DeployAsync("fn-2", actionsEnabled: true);

            var rows = await _repository.GetManyAsync(Tenant, [("fn-1", null), ("fn-1", "fn-1-v1"), ("fn-3", null)]);

            rows.Select(r => r.ItemId).Should().BeEquivalentTo(
                FunctionEventSettingsEntity.IdFor("fn-1", null), FunctionEventSettingsEntity.IdFor("fn-1", "fn-1-v1"));
            rows.Single(r => r.VersionId is null).ActionsEnabled.Should().BeFalse();
            (await _repository.GetManyAsync(Tenant, [])).Should().BeEmpty();
        }

        [Fact]
        public async Task Deploy_copies_the_Actions_switch_to_the_version()
        {
            await DeployAsync("fn-1", actionsEnabled: false);

            var version = await _repository.GetAsync(Tenant, "fn-1", "fn-1-v1");

            version!.ActionsEnabled.Should().BeFalse();
            version.ActiveTrigger.Should().BeNull();
            version.TriggerEvents.Should().Equal(Action);
        }
    }
}
