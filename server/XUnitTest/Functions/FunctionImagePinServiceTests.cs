using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The keep set is what stops a runner reclaiming an image. Everything here is about the two
    /// ways that goes wrong: unpinning an image something still needs, which costs a rebuild, and
    /// failing to unpin one nothing needs, which costs disk on every runner until the host dies.
    /// </summary>
    public class FunctionImagePinServiceTests
    {
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);
        private readonly List<string> _pinned = [];
        private readonly List<string> _released = [];

        private FunctionImagePinService Service()
        {
            _database.Setup(d => d.SetAddAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey _, RedisValue digest, CommandFlags __) =>
                {
                    _pinned.Add(digest.ToString());
                    return Task.FromResult(true);
                });
            _database.Setup(d => d.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey _, RedisValue digest, CommandFlags __) =>
                {
                    _released.Add(digest.ToString());
                    return Task.FromResult(true);
                });

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_database.Object);
            return new FunctionImagePinService(cache.Object, NullLogger<FunctionImagePinService>.Instance);
        }

        [Fact]
        public async Task Pinning_adds_the_digest()
        {
            await Service().PinAsync("sha256:a");

            _pinned.Should().Equal("sha256:a");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task A_missing_digest_is_not_pinned(string? digest)
        {
            // An empty member in the keep set matches nothing and never leaves again.
            await Service().PinAsync(digest);

            _pinned.Should().BeEmpty();
        }

        [Fact]
        public async Task Releasing_removes_each_distinct_digest_once()
        {
            var released = await Service().ReleaseAsync(["sha256:a", "sha256:a", "sha256:b", null, ""]);

            _released.Should().Equal("sha256:a", "sha256:b");
            released.Should().Be(2);
        }

        [Fact]
        public async Task A_digest_something_else_still_uses_stays_pinned()
        {
            var keep = new HashSet<string>(["sha256:shared"], StringComparer.Ordinal);

            var released = await Service().ReleaseAsync(["sha256:shared", "sha256:gone"], keep);

            _released.Should().Equal("sha256:gone");
            released.Should().Be(1);
        }

        [Fact]
        public async Task A_digest_that_was_not_pinned_is_not_counted_as_released()
        {
            var service = Service();
            _database.Setup(d => d.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);

            var released = await service.ReleaseAsync(["sha256:never-pinned"]);

            released.Should().Be(0);
        }

        [Fact]
        public async Task A_pin_that_cannot_be_written_does_not_fail_the_caller()
        {
            // The caller is a deploy that has already written its version: throwing here would
            // fail a deploy that has, in every way that matters, happened.
            var service = Service();
            _database.Setup(d => d.SetAddAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var act = async () => await service.PinAsync("sha256:a");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task One_digest_failing_to_release_does_not_abandon_the_rest()
        {
            var service = Service();
            _database.SetupSequence(d => d.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"))
                .ReturnsAsync(true);

            var released = await service.ReleaseAsync(["sha256:a", "sha256:b"]);

            released.Should().Be(1);
        }
    }
}
