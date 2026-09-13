using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Deleting a function used to leave most of it behind: build records, runs, run logs, and —
    /// the expensive one — the keep-set entries that stop every runner from ever reclaiming the
    /// images those builds produced. A deleted function went on costing disk on every host for as
    /// long as the host lived. These pin what a delete now removes, and the order it removes it in.
    /// </summary>
    public class FunctionPurgeServiceTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";

        private readonly Mock<IFunctionVersionRepository> _versions = new(MockBehavior.Loose);
        private readonly Mock<IFunctionBuildRepository> _builds = new(MockBehavior.Loose);
        private readonly Mock<IFunctionRunRepository> _runs = new(MockBehavior.Loose);
        private readonly Mock<IFunctionRunLogRepository> _logs = new(MockBehavior.Loose);
        private readonly Mock<IFunctionRunStatsRepository> _stats = new(MockBehavior.Loose);
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);

        private readonly List<string> _released = [];
        private readonly List<string> _keysDeleted = [];
        private readonly List<string> _cancelled = [];

        /// <summary>Every failure the purge swallowed, so a test that expects work to happen can
        /// say why it did not instead of just reporting an empty collection.</summary>
        public List<string> Failures { get; } = [];

        private sealed class CapturingLogger<T>(List<string> sink) : Microsoft.Extensions.Logging.ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
                {
                    sink.Add($"{formatter(state, exception)} :: {exception?.GetType().Name}: {exception?.Message}");
                }
            }
        }

        private static FunctionVersionEntity Version(int n, string digest) =>
            new() { ItemId = $"v{n}", FunctionId = FunctionId, Number = n, ImageDigest = digest };

        private static FunctionBuildEntity Build(string id, string? digest, BuildStatus status = BuildStatus.Succeeded) =>
            new() { ItemId = id, FunctionId = FunctionId, ImageDigest = digest, Status = status };

        private static FunctionRunEntity Run(string id) =>
            new() { ItemId = id, FunctionId = FunctionId, Status = RunStatus.Running };

        private FunctionPurgeService Service(
            IReadOnlyList<FunctionVersionEntity>? versions = null,
            IReadOnlyList<FunctionBuildEntity>? builds = null,
            IReadOnlyList<FunctionRunEntity>? activeRuns = null)
        {
            _versions.Setup(v => v.GetAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(versions ?? []);
            _versions.Setup(v => v.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(versions?.Count ?? 0);

            _builds.Setup(b => b.GetAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(builds ?? []);
            _builds.Setup(b => b.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(builds?.Count ?? 0);

            _runs.Setup(r => r.GetAllAsync(Tenant, It.IsAny<FunctionRunFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(((IReadOnlyList<FunctionRunEntity>)(activeRuns ?? []), (long)(activeRuns?.Count ?? 0)));
            _runs.Setup(r => r.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(7);
            _logs.Setup(l => l.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(42);

            _database.Setup(d => d.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey _, RedisValue digest, CommandFlags __) =>
                {
                    _released.Add(digest.ToString());
                    return Task.FromResult(true);
                });
            _database.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey key, CommandFlags _) =>
                {
                    _keysDeleted.Add(key.ToString());
                    return Task.FromResult(true);
                });
            _database.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                    It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey key, RedisValue _, Expiration __, ValueCondition ___, CommandFlags ____) =>
                {
                    _cancelled.Add(key.ToString());
                    return Task.FromResult(true);
                });

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_database.Object);

            var pins = new FunctionImagePinService(cache.Object, NullLogger<FunctionImagePinService>.Instance);

            return new FunctionPurgeService(
                _versions.Object, _builds.Object, _runs.Object, _logs.Object, _stats.Object,
                pins, cache.Object, new CapturingLogger<FunctionPurgeService>(Failures));
        }

        [Fact]
        public async Task Everything_the_function_owns_is_removed()
        {
            var service = Service(
                versions: [Version(1, "sha256:a")],
                builds: [Build("b1", "sha256:b")]);

            var report = await service.PurgeAsync(Tenant, FunctionId);

            report.Versions.Should().Be(1);
            report.Builds.Should().Be(1);
            report.Runs.Should().Be(7);
            report.RunLogs.Should().Be(42);

            _versions.Verify(v => v.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()), Times.Once);
            _builds.Verify(b => b.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()), Times.Once);
            _runs.Verify(r => r.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()), Times.Once);
            _logs.Verify(l => l.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()), Times.Once);
            _stats.Verify(s => s.DeleteAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Images_from_versions_and_from_test_only_builds_are_both_released()
        {
            // The leak was exactly this asymmetry: a version's digest had a remover, a build's
            // digest had none, and a function that was only ever tested pinned images for good.
            var service = Service(
                versions: [Version(1, "sha256:deployed")],
                builds: [Build("b1", "sha256:tested"), Build("b2", "sha256:deployed")]);

            var report = await service.PurgeAsync(Tenant, FunctionId);

            _released.Should().BeEquivalentTo(["sha256:deployed", "sha256:tested"]);
            report.ImagesReleased.Should().Be(2);
        }

        [Fact]
        public async Task A_digest_is_released_once_however_many_records_name_it()
        {
            var service = Service(
                versions: [Version(1, "sha256:same"), Version(2, "sha256:same")],
                builds: [Build("b1", "sha256:same")]);

            await service.PurgeAsync(Tenant, FunctionId);

            _released.Should().ContainSingle().Which.Should().Be("sha256:same");
        }

        [Fact]
        public async Task Builds_that_never_produced_an_image_release_nothing()
        {
            var service = Service(builds: [Build("b1", null, BuildStatus.Failed), Build("b2", "")]);

            await service.PurgeAsync(Tenant, FunctionId);

            _released.Should().BeEmpty();
        }

        [Fact]
        public async Task In_flight_runs_are_cancelled()
        {
            // A sandbox mid-run would otherwise keep going and report against a function that has
            // gone, burning a slot on the runner for a result nobody will read.
            var service = Service(activeRuns: [Run("r1"), Run("r2")]);

            var report = await service.PurgeAsync(Tenant, FunctionId);

            Failures.Should().BeEmpty();
            report.RunsCancelled.Should().Be(2);
            _cancelled.Should().BeEquivalentTo([FunctionQueueKeys.Cancel("r1"), FunctionQueueKeys.Cancel("r2")]);
        }

        [Fact]
        public async Task Only_non_terminal_runs_are_asked_for()
        {
            var service = Service();

            FunctionRunFilter? used = null;
            _runs.Setup(r => r.GetAllAsync(Tenant, It.IsAny<FunctionRunFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback((string _, FunctionRunFilter f, int __, int ___, CancellationToken ____) => used = f)
                .ReturnsAsync(((IReadOnlyList<FunctionRunEntity>)[], 0L));

            await service.PurgeAsync(Tenant, FunctionId);

            used!.ActiveOnly.Should().BeTrue();
            used.FunctionId.Should().Be(FunctionId);
        }

        [Fact]
        public async Task The_source_bundle_of_an_unstarted_build_is_dropped()
        {
            // The runner fails a build cleanly when its bundle is gone, which is the right end for
            // a build queued for a function that no longer exists.
            var service = Service(builds:
            [
                Build("queued", null, BuildStatus.Queued),
                Build("building", null, BuildStatus.Building),
                Build("done", "sha256:a"),
            ]);

            await service.PurgeAsync(Tenant, FunctionId);

            _keysDeleted.Should().Contain(FunctionQueueKeys.Source("queued"));
            _keysDeleted.Should().Contain(FunctionQueueKeys.Source("building"));
            _keysDeleted.Should().NotContain(FunctionQueueKeys.Source("done"));
        }

        [Fact]
        public async Task The_concurrency_key_is_deleted()
        {
            await Service().PurgeAsync(Tenant, FunctionId);

            _keysDeleted.Should().Contain(FunctionQueueKeys.Concurrency(FunctionId));
        }

        [Fact]
        public async Task Redis_being_unreachable_does_not_stop_the_records_going()
        {
            // Mongo is the part a tenant can see. A purge that gave up because Redis was down
            // would leave the function listed and undeletable.
            var service = Service(versions: [Version(1, "sha256:a")], builds: [Build("b1", "sha256:b")]);

            _database.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
            _database.Setup(d => d.SetRemoveAsync(FunctionQueueKeys.ImagesKeep, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var report = await service.PurgeAsync(Tenant, FunctionId);

            report.Versions.Should().Be(1);
            report.ImagesReleased.Should().Be(0);
            _runs.Verify(r => r.DeleteAllForFunctionAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()), Times.Once);

            // Silently leaving images pinned on every runner is the failure mode this whole change
            // exists to remove, so it has to be loud even when it is survivable.
            Failures.Should().NotBeEmpty();
        }

        [Fact]
        public async Task Purging_a_function_with_nothing_to_purge_is_not_an_error()
        {
            var report = await Service().PurgeAsync(Tenant, FunctionId);

            report.Versions.Should().Be(0);
            report.ImagesReleased.Should().Be(0);
            report.RunsCancelled.Should().Be(0);
        }

        [Fact]
        public async Task An_empty_function_id_purges_nothing_at_all()
        {
            // A purge keyed on an empty id would match every record in the tenant.
            var report = await Service().PurgeAsync(Tenant, "  ");

            report.Should().Be(FunctionPurgeReport.Empty);
            _runs.Verify(r => r.DeleteAllForFunctionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _versions.Verify(v => v.DeleteAllForFunctionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
