using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A test build's image is local to one runner and deleted after its run, so the build record
    /// must never be handed out for reuse — not to the next test, and not to a deploy. Against a
    /// real MongoDB (<c>BLOCKS_FUNCTIONS_TEST_MONGO</c>, default <c>mongodb://localhost:27017</c>).
    /// </summary>
    public sealed class FunctionBuildReuseTests : IDisposable
    {
        private const string Tenant = "tenant-builds";
        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_fn_builds_" + Guid.NewGuid().ToString("N");
        private readonly FunctionBuildRepository _repository;

        public FunctionBuildReuseTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            _client = new MongoClient(url);
            var collection = _client.GetDatabase(_databaseName)
                .GetCollection<FunctionBuildEntity>(FunctionsConstants.FunctionBuildsCollection);
            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<FunctionBuildEntity>(It.IsAny<string>(), It.IsAny<string>())).Returns(collection);
            _repository = new FunctionBuildRepository(provider.Object);
        }

        public void Dispose() => _client.DropDatabase(_databaseName);

        private Task Seed(string id, BuildStatus status, bool ephemeral, DateTime created) =>
            _repository.CreateAsync(Tenant, new FunctionBuildEntity
            {
                ItemId = id, FunctionId = "fn-1", SourceHash = "h1", Status = status, Ephemeral = ephemeral,
                ImageDigest = ephemeral ? "sha256:local" : "127.0.0.1:5000/fn/fn-1@sha256:pushed",
                CreatedDate = created, LastUpdatedDate = created,
            });

        [Fact]
        public async Task A_newer_test_build_is_never_returned_for_reuse()
        {
            await Seed("deploy-build", BuildStatus.Succeeded, ephemeral: false, DateTime.UtcNow.AddHours(-1));
            await Seed("test-build", BuildStatus.Succeeded, ephemeral: true, DateTime.UtcNow);

            var reused = await _repository.GetSucceededBySourceHashAsync(Tenant, "fn-1", "h1");

            reused!.ItemId.Should().Be("deploy-build");
        }

        [Fact]
        public async Task Only_test_builds_means_nothing_to_reuse()
        {
            await Seed("test-build", BuildStatus.Succeeded, ephemeral: true, DateTime.UtcNow);
            await Seed("test-building", BuildStatus.Building, ephemeral: true, DateTime.UtcNow);

            (await _repository.GetSucceededBySourceHashAsync(Tenant, "fn-1", "h1")).Should().BeNull();
            // A deploy must not wait on a test's in-flight build either: that image is never pushed.
            (await _repository.GetInProgressBySourceHashAsync(Tenant, "fn-1", "h1")).Should().BeNull();
        }
    }
}
