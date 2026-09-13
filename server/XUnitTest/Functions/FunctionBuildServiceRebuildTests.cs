using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
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
    /// The escape hatch from a cached build.
    /// <para>
    /// A succeeded build is reused for ever for the same source hash, which is what makes Test
    /// then Deploy cheap (DECISIONS D3) — and what left a function stuck when the image behind
    /// that build was wrong rather than missing. The only way out was to edit the source until
    /// its hash changed. Rebuild is the way out that does not involve lying to the editor.
    /// </para>
    /// </summary>
    public class FunctionBuildServiceRebuildTests
    {
        private const string Tenant = "t1";

        private readonly Mock<IFunctionBuildRepository> _builds = new(MockBehavior.Loose);
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);
        private readonly List<FunctionBuildEntity> _created = [];

        /// <summary>Which streams the service pushed a job onto, whatever overload it used —
        /// the Redis client changes those between versions and the test should not care.</summary>
        private IEnumerable<string> Streamed => _database.Invocations
            .Where(i => i.Method.Name == "StreamAddAsync")
            .Select(i => i.Arguments[0].ToString()!);

        private static FunctionEntity Function() => new()
        {
            ItemId = "fn_1",
            SourceHash = "hash_1",
            Source = new FunctionSource
            {
                IndexJs = "export default async () => 1;",
                PackageJson = """{"type":"module"}""",
            },
        };

        private static FunctionBuildEntity Succeeded() => new()
        {
            ItemId = "build_old",
            FunctionId = "fn_1",
            SourceHash = "hash_1",
            Status = BuildStatus.Succeeded,
            ImageDigest = "sha256:cached",
        };

        private FunctionBuildService Service(
            FunctionBuildEntity? succeeded = null, FunctionBuildEntity? inProgress = null)
        {
            _builds.Setup(b => b.GetSucceededBySourceHashAsync(Tenant, "fn_1", "hash_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(succeeded);
            _builds.Setup(b => b.GetInProgressBySourceHashAsync(Tenant, "fn_1", "hash_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(inProgress);
            _builds.Setup(b => b.CreateAsync(Tenant, It.IsAny<FunctionBuildEntity>(), It.IsAny<CancellationToken>()))
                .Returns((string _, FunctionBuildEntity build, CancellationToken __) =>
                {
                    _created.Add(build);
                    return Task.CompletedTask;
                });
            _builds.Setup(b => b.GetByIdAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FunctionBuildEntity?)null);

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_database.Object);

            return new FunctionBuildService(
                _builds.Object, cache.Object,
                new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
                NullLogger<FunctionBuildService>.Instance);
        }

        [Fact]
        public async Task Without_a_rebuild_a_cached_success_is_reused()
        {
            var build = await Service(succeeded: Succeeded())
                .EnsureImageAsync(Tenant, Function(), default, waitSecondsOverride: 0);

            build.ItemId.Should().Be("build_old");
            _created.Should().BeEmpty();
        }

        [Fact]
        public async Task A_rebuild_ignores_the_cached_success_and_queues_a_build()
        {
            var build = await Service(succeeded: Succeeded())
                .EnsureImageAsync(Tenant, Function(), default, waitSecondsOverride: 0, forceRebuild: true);

            build.ItemId.Should().NotBe("build_old");
            build.Status.Should().Be(BuildStatus.Queued);
            _created.Should().ContainSingle();
            Streamed.Should().ContainSingle().Which.Should().Be(FunctionQueueKeys.BuildsStream);
        }

        [Fact]
        public async Task A_rebuild_is_not_even_looked_up_in_the_cache()
        {
            await Service(succeeded: Succeeded())
                .EnsureImageAsync(Tenant, Function(), default, waitSecondsOverride: 0, forceRebuild: true);

            _builds.Verify(b => b.GetSucceededBySourceHashAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_rebuild_joins_a_build_already_running_for_the_same_source()
        {
            // It is already building exactly what was asked for. Queuing a second one would have
            // two runners build the same image and race to push the same tag.
            var inFlight = new FunctionBuildEntity
            {
                ItemId = "build_inflight",
                FunctionId = "fn_1",
                SourceHash = "hash_1",
                Status = BuildStatus.Building,
                LastUpdatedDate = DateTime.UtcNow,
            };

            var build = await Service(succeeded: Succeeded(), inProgress: inFlight)
                .EnsureImageAsync(Tenant, Function(), default, waitSecondsOverride: 0, forceRebuild: true);

            build.ItemId.Should().Be("build_inflight");
            _created.Should().BeEmpty();
        }

        [Fact]
        public async Task The_queued_build_carries_the_source_hash_it_was_asked_for()
        {
            await Service().EnsureImageAsync(Tenant, Function(), default, waitSecondsOverride: 0, forceRebuild: true);

            _created.Should().ContainSingle().Which.SourceHash.Should().Be("hash_1");
        }
    }
}
