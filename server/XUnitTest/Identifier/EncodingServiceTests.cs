using Blocks.Genesis;
using DomainService.Shared.Utilities;
using FluentAssertions;
using DomainService.Shared;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Identifier
{
    /// <summary>
    /// Unit tests for <see cref="EncodingService"/>. Mongo is reached through
    /// <c>GetDatabase(connectionString, databaseName).GetCollection&lt;BlocksGuid&gt;("BlocksGuids")</c>,
    /// so the database and collection are both mocked and the base26 encoding, the reuse of an
    /// already stored value, the collision loop and the truncation are exercised in memory.
    /// </summary>
    public class EncodingServiceTests
    {
        private const string Collection = "BlocksGuids";

        private readonly Mock<IMongoCollection<BlocksGuid>> _collection = new();
        private readonly List<BlocksGuid> _inserted = [];

        private EncodingService Build(
            IEnumerable<BlocksGuid>? existingByOriginal = null,
            Queue<bool>? existsResults = null)
        {
            var db = new Mock<IMongoDatabase>();
            db.Setup(d => d.GetCollection<BlocksGuid>(Collection, It.IsAny<MongoCollectionSettings>()))
              .Returns(_collection.Object);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetDatabase("conn", "root", It.IsAny<bool>())).Returns(db.Object);

            var secret = new Mock<IBlocksSecret>();
            secret.SetupGet(s => s.DatabaseConnectionString).Returns("conn");
            secret.SetupGet(s => s.RootDatabaseName).Returns("root");

            // Two reads share the same overload: the lookup by OriginalValue and the collision
            // check by EncodedValue. The queue lets a test drive the collision loop, and the
            // lookup result is returned first.
            var byOriginal = (existingByOriginal ?? []).ToList();
            var first = true;
            _collection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BlocksGuid>>(),
                    It.IsAny<FindOptions<BlocksGuid, BlocksGuid>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (first)
                    {
                        first = false;
                        return Cursor(byOriginal);
                    }

                    var collides = existsResults is { Count: > 0 } && existsResults.Dequeue();
                    return Cursor(collides ? [new BlocksGuid { EncodedValue = "x" }] : []);
                });

            _collection
                .Setup(c => c.InsertOneAsync(It.IsAny<BlocksGuid>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .Returns((BlocksGuid g, InsertOneOptions _, CancellationToken _) =>
                {
                    _inserted.Add(g);
                    return Task.CompletedTask;
                });

            return new EncodingService(provider.Object, secret.Object);
        }

        private static IAsyncCursor<BlocksGuid> Cursor(List<BlocksGuid> items)
        {
            var cursor = new Mock<IAsyncCursor<BlocksGuid>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task EncodeToBase26Async_ReturnsEmptyForBlankInput(string? input)
        {
            var sut = Build();

            (await sut.EncodeToBase26Async(input!, "tenant-1", 6)).Should().BeEmpty();

            _collection.Verify(c => c.InsertOneAsync(
                It.IsAny<BlocksGuid>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EncodeToBase26Async_ReusesAnAlreadyStoredEncoding()
        {
            var sut = Build(existingByOriginal: [new BlocksGuid { OriginalValue = "42", EncodedValue = "abcdefgh" }]);

            var result = await sut.EncodeToBase26Async("42", "tenant-1", 4);

            result.Should().Be("abcd", "a stored encoding is reused and truncated, not regenerated");
            _collection.Verify(c => c.InsertOneAsync(
                It.IsAny<BlocksGuid>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EncodeToBase26Async_EncodesANumericInputToLowercaseLetters()
        {
            var sut = Build();

            var result = await sut.EncodeToBase26Async("100", "tenant-1", 10);

            result.Should().NotBeEmpty();
            result.Should().MatchRegex("^[a-z]+$");
        }

        [Fact]
        public async Task EncodeToBase26Async_EncodesZeroToTheFirstLetter()
        {
            var sut = Build();

            (await sut.EncodeToBase26Async("0", "tenant-1", 10)).Should().Be("a");
        }

        [Fact]
        public async Task EncodeToBase26Async_TreatsANegativeNumberAsItsAbsoluteValue()
        {
            var negative = await Build().EncodeToBase26Async("-100", "tenant-1", 10);
            var positive = await Build().EncodeToBase26Async("100", "tenant-1", 10);

            negative.Should().Be(positive);
        }

        [Fact]
        public async Task EncodeToBase26Async_EncodesAGuidInput()
        {
            var sut = Build();

            var result = await sut.EncodeToBase26Async(Guid.NewGuid().ToString(), "tenant-1", 12);

            result.Should().MatchRegex("^[a-z]+$");
            result.Length.Should().BeLessThanOrEqualTo(12);
        }

        [Fact]
        public async Task EncodeToBase26Async_EncodesAnArbitraryStringByFallingBackToANewGuid()
        {
            var sut = Build();

            var result = await sut.EncodeToBase26Async("not-a-number-or-guid", "tenant-1", 8);

            result.Should().MatchRegex("^[a-z]+$");
            result.Length.Should().BeLessThanOrEqualTo(8);
        }

        [Fact]
        public async Task EncodeToBase26Async_TruncatesToTheRequestedLength()
        {
            var sut = Build();

            var result = await sut.EncodeToBase26Async(Guid.NewGuid().ToString(), "tenant-1", 3);

            result.Should().HaveLength(3);
        }

        [Fact]
        public async Task EncodeToBase26Async_RetriesUntilTheEncodingIsUnique()
        {
            // First candidate collides, second is free, so the loop runs twice.
            var sut = Build(existsResults: new Queue<bool>([true, false]));

            var result = await sut.EncodeToBase26Async("500", "tenant-1", 10);

            result.Should().NotBeEmpty();
            _collection.Verify(c => c.FindAsync(
                It.IsAny<FilterDefinition<BlocksGuid>>(),
                It.IsAny<FindOptions<BlocksGuid, BlocksGuid>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task EncodeToBase26Async_StoresTheTruncatedValueAgainstTheTenantGroup()
        {
            var sut = Build();

            var returned = await sut.EncodeToBase26Async("77", "tenant-9", 2);

            _inserted.Should().ContainSingle();
            var row = _inserted[0];
            row.OriginalValue.Should().Be("77");
            row.TenantGroupId.Should().Be("tenant-9");
            row.EncodedValue.Should().Be(returned).And.HaveLength(2);
            row.ItemId.Should().NotBeNullOrWhiteSpace();
        }
    }
}
