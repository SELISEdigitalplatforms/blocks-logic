using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Retention deletes immutable history, so almost every test here is about what it must
    /// <i>refuse</i> to delete. The cap is the easy part.
    /// </summary>
    public class FunctionVersionRetentionServiceTests
    {
        private readonly Mock<IFunctionVersionRepository> _versions = new(MockBehavior.Loose);
        private readonly Mock<IFunctionRunRepository> _runs = new(MockBehavior.Loose);
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);
        private readonly List<string> _deleted = [];
        private readonly List<string> _released = [];

        private static FunctionVersionEntity Version(int number, string digest = "sha256:x") => new()
        {
            ItemId = $"v{number}",
            FunctionId = "fn_1",
            Number = number,
            ImageDigest = digest,
        };

        private FunctionVersionRetentionService Service(
            IReadOnlyList<FunctionVersionEntity> versions,
            IReadOnlySet<string>? inFlight = null,
            int? cap = null)
        {
            _versions
                .Setup(v => v.GetAllForFunctionAsync("t1", "fn_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(versions.OrderByDescending(v => v.Number).ToList());
            _versions
                .Setup(v => v.DeleteManyAsync("t1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, IReadOnlyCollection<string> ids, CancellationToken __) =>
                {
                    _deleted.AddRange(ids);
                    return Task.FromResult((long)ids.Count);
                });
            _runs
                .Setup(r => r.GetVersionIdsWithActiveRunsAsync("t1", "fn_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(inFlight ?? new HashSet<string>(StringComparer.Ordinal));
            _database
                .Setup(d => d.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey _, RedisValue digest, CommandFlags __) =>
                {
                    _released.Add(digest.ToString());
                    return Task.FromResult(true);
                });

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_database.Object);

            var settings = new Dictionary<string, string?>();
            if (cap.HasValue) settings["Functions:MaxVersionsPerFunction"] = cap.Value.ToString();

            return new FunctionVersionRetentionService(
                _versions.Object, _runs.Object, cache.Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                NullLogger<FunctionVersionRetentionService>.Instance);
        }

        [Fact]
        public async Task Keeps_the_newest_ten_and_deletes_the_rest()
        {
            var versions = Enumerable.Range(1, 13).Select(n => Version(n, $"sha256:{n}")).ToList();

            var pruned = await Service(versions).PruneAsync("t1", "fn_1", activeVersionId: "v13");

            pruned.Should().Be(3);
            _deleted.Should().BeEquivalentTo(["v1", "v2", "v3"]);
        }

        [Fact]
        public async Task Does_nothing_when_the_history_is_within_the_cap()
        {
            var versions = Enumerable.Range(1, 10).Select(n => Version(n, $"sha256:{n}")).ToList();

            var pruned = await Service(versions).PruneAsync("t1", "fn_1", activeVersionId: "v10");

            pruned.Should().Be(0);
            _deleted.Should().BeEmpty();
            _versions.Verify(
                v => v.DeleteManyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Never_deletes_the_active_version_even_when_it_is_old()
        {
            // After a rollback the live version can be an old one; deleting it would leave the
            // function pointing at a record that no longer exists.
            var versions = Enumerable.Range(1, 13).Select(n => Version(n, $"sha256:{n}")).ToList();

            await Service(versions).PruneAsync("t1", "fn_1", activeVersionId: "v2");

            _deleted.Should().NotContain("v2");
            _deleted.Should().BeEquivalentTo(["v1", "v3"]);
        }

        [Fact]
        public async Task Never_deletes_a_version_a_running_run_still_needs()
        {
            var versions = Enumerable.Range(1, 13).Select(n => Version(n, $"sha256:{n}")).ToList();
            var inFlight = new HashSet<string>(["v1"], StringComparer.Ordinal);

            await Service(versions, inFlight).PruneAsync("t1", "fn_1", activeVersionId: "v13");

            _deleted.Should().NotContain("v1");
            _deleted.Should().BeEquivalentTo(["v2", "v3"]);
        }

        [Fact]
        public async Task Releases_the_pruned_images_so_the_runner_can_reclaim_them()
        {
            var versions = Enumerable.Range(1, 12).Select(n => Version(n, $"sha256:{n}")).ToList();

            await Service(versions).PruneAsync("t1", "fn_1", activeVersionId: "v12");

            _released.Should().BeEquivalentTo(["sha256:1", "sha256:2"]);
        }

        [Fact]
        public async Task Keeps_an_image_a_surviving_version_shares()
        {
            // Builds are content-addressed, so identical source means an identical digest:
            // releasing it because an old version was pruned would pull it out from under the
            // version still using it.
            var versions = Enumerable.Range(1, 12).Select(n => Version(n, "sha256:same")).ToList();

            await Service(versions).PruneAsync("t1", "fn_1", activeVersionId: "v12");

            _deleted.Should().BeEquivalentTo(["v1", "v2"]);
            _released.Should().BeEmpty();
        }

        [Fact]
        public async Task The_cap_is_configurable_and_cannot_be_driven_below_one()
        {
            var versions = Enumerable.Range(1, 5).Select(n => Version(n, $"sha256:{n}")).ToList();

            await Service(versions, cap: 0).PruneAsync("t1", "fn_1", activeVersionId: "v5");

            // Floored at 1, and the active version is kept regardless.
            _deleted.Should().BeEquivalentTo(["v1", "v2", "v3", "v4"]);
        }
    }
}
