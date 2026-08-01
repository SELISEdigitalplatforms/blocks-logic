using Blocks.Genesis;
using Cloud.DomainService.Models;
using Cloud.DomainService.Repositories;
using Cloud.DomainService.Requests;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Cloud
{
    /// <summary>
    /// Unit tests for <see cref="ApiEndpointConfigRepository"/>. Permissions are stored with the
    /// endpoint encoded into a single `group::controller::method` string, so the two things worth
    /// pinning are the filter built for each combination of controller and method, and the way that
    /// string is taken apart again on the way out. The write paths report whether anything actually
    /// changed rather than whether the command was accepted.
    /// </summary>
    public class ApiEndpointConfigRepositoryTests
    {
        private const string CollectionName = "Permissions";

        private readonly Mock<IDbContextProvider> _provider = new();
        private readonly Mock<IMongoDatabase> _db = new();
        private readonly Mock<IMongoCollection<ApiEndpointConfig>> _permissions = new();
        private readonly ApiEndpointConfigRepository _sut;

        public ApiEndpointConfigRepositoryTests()
        {
            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns("mongodb://localhost");
            secret.SetupGet(s => s.RootDatabaseName).Returns("root");

            _provider.Setup(p => p.GetDatabase("mongodb://localhost", "root")).Returns(_db.Object);
            _db.Setup(d => d.GetCollection<ApiEndpointConfig>(CollectionName, null)).Returns(_permissions.Object);

            SetupFind();
            SetupCount(0);

            _sut = new ApiEndpointConfigRepository(_provider.Object, secret.Object);
        }

        private FilterDefinition<ApiEndpointConfig>? _lastFilter;

        private void SetupFind(params ApiEndpointConfig[] documents)
        {
            var cursor = new Mock<IAsyncCursor<ApiEndpointConfig>>();
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(documents.Length > 0)
                  .ReturnsAsync(false);
            cursor.SetupGet(c => c.Current).Returns(documents);

            _permissions.Setup(c => c.FindAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<FindOptions<ApiEndpointConfig, ApiEndpointConfig>>(),
                          It.IsAny<CancellationToken>()))
                      .Callback<FilterDefinition<ApiEndpointConfig>, FindOptions<ApiEndpointConfig, ApiEndpointConfig>, CancellationToken>(
                          (f, _, _) => _lastFilter = f)
                      .ReturnsAsync(cursor.Object);
        }

        private void SetupCount(long count) =>
            _permissions.Setup(c => c.CountDocumentsAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<CountOptions>(),
                          It.IsAny<CancellationToken>()))
                      .ReturnsAsync(count);

        /// <summary>Renders the filter the repository built, so its shape can be asserted.</summary>
        private string RenderedFilter() =>
            _lastFilter!.Render(new RenderArgs<ApiEndpointConfig>(
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry.GetSerializer<ApiEndpointConfig>(),
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();

        private static ApiEndpointConfig Config(string resource = "iam::Users::Create") => new()
        {
            ItemId = "perm-1",
            Resource = resource,
            ResourceGroup = "iam",
            Name = "Create user",
            IsCaptchaRequired = false,
            IsMFARequired = false,
        };

        private static GetApiEndpointConfigsRequest Request(
            string? group = null, string? controller = null, string? method = null) => new()
            {
                Page = 0,
                PageSize = 10,
                Filter = new ApiEndpointConfigFilter
                {
                    ResourceGroup = group,
                    Controller = controller,
                    Method = method,
                },
            };

        // ---- reading ----

        [Fact]
        public async Task GetListAsync_SplitsTheResourceIntoControllerAndMethod()
        {
            SetupFind(Config("iam::Users::Create"));
            SetupCount(1);

            var (items, count) = await _sut.GetListAsync(Request());

            count.Should().Be(1);
            items.Should().ContainSingle();
            items[0].Controller.Should().Be("Users");
            items[0].Method.Should().Be("Create");
            items[0].Resource.Should().Be("iam::Users::Create");
        }

        [Fact]
        public async Task GetListAsync_LeavesTheHalvesEmptyForAResourceThatIsNotEncoded()
        {
            SetupFind(Config("just-a-name"));

            var (items, _) = await _sut.GetListAsync(Request());

            items[0].Controller.Should().BeEmpty();
            items[0].Method.Should().BeEmpty();
        }

        [Fact]
        public async Task GetListAsync_HandlesAResourceWithOnlyAController()
        {
            SetupFind(Config("iam::Users"));

            var (items, _) = await _sut.GetListAsync(Request());

            items[0].Controller.Should().Be("Users");
            items[0].Method.Should().BeEmpty();
        }

        [Fact]
        public async Task GetListAsync_SurvivesAResourceThatIsNull()
        {
            SetupFind(Config(null!));

            var (items, _) = await _sut.GetListAsync(Request());

            items[0].Controller.Should().BeEmpty();
            items[0].Method.Should().BeEmpty();
        }

        [Fact]
        public async Task GetListAsync_ReportsTheTotalSeparatelyFromThePage()
        {
            SetupFind(Config());
            SetupCount(250);

            var (items, count) = await _sut.GetListAsync(Request());

            // The page is one document, but the caller needs the whole count to paginate.
            items.Should().ContainSingle();
            count.Should().Be(250);
        }

        [Fact]
        public async Task GetListAsync_ReturnsAnEmptyPageWhenNothingMatches()
        {
            SetupFind();

            var (items, count) = await _sut.GetListAsync(Request());

            items.Should().BeEmpty();
            count.Should().Be(0);
        }

        // ---- the filter ----

        [Fact]
        public async Task GetListAsync_FiltersByResourceGroupWhenOneIsGiven()
        {
            await _sut.GetListAsync(Request(group: "iam"));

            RenderedFilter().Should().Contain("ResourceGroup").And.Contain("iam");
        }

        [Fact]
        public async Task GetListAsync_MatchesBothHalvesWhenControllerAndMethodAreGiven()
        {
            await _sut.GetListAsync(Request(controller: "Users", method: "Create"));

            // Anchored at both ends so "Users::Create" cannot match "Users::CreateBulk".
            RenderedFilter().Should().Contain("^[^:]+::Users::Create$");
        }

        [Fact]
        public async Task GetListAsync_MatchesAControllerPrefixWhenOnlyAControllerIsGiven()
        {
            await _sut.GetListAsync(Request(controller: "Users"));

            RenderedFilter().Should().Contain("^[^:]+::Users::");
        }

        [Fact]
        public async Task GetListAsync_MatchesAMethodSuffixWhenOnlyAMethodIsGiven()
        {
            await _sut.GetListAsync(Request(method: "Create"));

            RenderedFilter().Should().Contain("::Create$");
        }

        [Fact]
        public async Task GetListAsync_EscapesRegexMetacharactersInTheFilter()
        {
            // A controller name containing regex syntax must be matched literally.
            await _sut.GetListAsync(Request(controller: "Users.v2+"));

            // The rendered document is JSON, so each regex backslash appears escaped.
            RenderedFilter().Should().Contain(@"Users\\.v2\\+");
        }

        [Fact]
        public async Task GetListAsync_AppliesNoFilterWhenNoneIsGiven()
        {
            await _sut.GetListAsync(new GetApiEndpointConfigsRequest { Page = 0, PageSize = 10 });

            RenderedFilter().Should().Be("{ }");
        }

        // ---- writing ----

        [Fact]
        public async Task UpdateAsync_ReportsSuccessWhenTheEndpointChanged()
        {
            _permissions.Setup(c => c.UpdateOneAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateOptions>(),
                          It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            (await _sut.UpdateAsync("tenant-1", "perm-1", true, true, "user-9")).Should().BeTrue();
        }

        [Fact]
        public async Task UpdateAsync_ReportsFailureWhenNothingChanged()
        {
            _permissions.Setup(c => c.UpdateOneAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateOptions>(),
                          It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new UpdateResult.Acknowledged(1, 0, null));

            // Matching a row is not the same as changing it.
            (await _sut.UpdateAsync("tenant-1", "perm-1", true, true, "user-9")).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateAsync_StampsTheFlagsAndTheEditor()
        {
            UpdateDefinition<ApiEndpointConfig>? applied = null;
            _permissions.Setup(c => c.UpdateOneAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateOptions>(),
                          It.IsAny<CancellationToken>()))
                      .Callback<FilterDefinition<ApiEndpointConfig>, UpdateDefinition<ApiEndpointConfig>, UpdateOptions, CancellationToken>(
                          (_, u, _, _) => applied = u)
                      .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            await _sut.UpdateAsync("tenant-1", "perm-1", true, false, "user-9");

            var rendered = applied!.Render(new RenderArgs<ApiEndpointConfig>(
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry.GetSerializer<ApiEndpointConfig>(),
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();
            rendered.Should().Contain("IsCaptchaRequired");
            rendered.Should().Contain("IsMFARequired");
            rendered.Should().Contain("user-9");
        }

        [Fact]
        public async Task BulkUpdateAsync_ReportsHowManyEndpointsChanged()
        {
            _permissions.Setup(c => c.UpdateManyAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateOptions>(),
                          It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new UpdateResult.Acknowledged(5, 3, null));

            var changed = await _sut.BulkUpdateAsync(
                "tenant-1", ["a", "b", "c", "d", "e"], true, true, "user-9");

            // Five matched, three actually differed.
            changed.Should().Be(3);
        }

        [Fact]
        public async Task BulkUpdateAsync_TargetsExactlyTheIdentifiersGiven()
        {
            FilterDefinition<ApiEndpointConfig>? applied = null;
            _permissions.Setup(c => c.UpdateManyAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateOptions>(),
                          It.IsAny<CancellationToken>()))
                      .Callback<FilterDefinition<ApiEndpointConfig>, UpdateDefinition<ApiEndpointConfig>, UpdateOptions, CancellationToken>(
                          (f, _, _, _) => applied = f)
                      .ReturnsAsync(new UpdateResult.Acknowledged(2, 2, null));

            await _sut.BulkUpdateAsync("tenant-1", ["perm-1", "perm-2"], false, false, "user-9");

            var rendered = applied!.Render(new RenderArgs<ApiEndpointConfig>(
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry.GetSerializer<ApiEndpointConfig>(),
                MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();
            rendered.Should().Contain("perm-1").And.Contain("perm-2");
        }

        [Fact]
        public async Task BulkUpdateAsync_ReportsNothingChangedForAnEmptySelection()
        {
            _permissions.Setup(c => c.UpdateManyAsync(
                          It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                          It.IsAny<UpdateOptions>(),
                          It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            (await _sut.BulkUpdateAsync("tenant-1", [], true, true, "user-9")).Should().Be(0);
        }

        [Fact]
        public async Task EveryCallGoesToTheRootDatabase()
        {
            await _sut.GetListAsync(Request());

            // Permissions are global rather than per-tenant, so this must not follow the caller.
            _provider.Verify(p => p.GetDatabase("mongodb://localhost", "root"), Times.AtLeastOnce);
            _provider.Verify(p => p.GetDatabase(It.IsAny<string>()), Times.Never);
        }
    }
}
