using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Entities;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The background half of a delete. What these pin: the purge repeats until nothing that was
    /// in flight at delete time can still add to what the function left behind; only then does
    /// the document go; a live function is never purged; and losing the Redis queue loses nothing.
    /// </summary>
    public class FunctionDeletionWorkerTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";
        private static readonly string Member = FunctionWorkerQueueKeys.PendingDeletionMember(Tenant, FunctionId);
        private static readonly DateTime RequestedAt = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IDatabase> _db = new(MockBehavior.Loose);
        private readonly Mock<IFunctionRepository> _functions = new(MockBehavior.Loose);
        private readonly Mock<IFunctionPurgeService> _purge = new(MockBehavior.Loose);
        private readonly Mock<IFunctionAuditService> _audit = new(MockBehavior.Loose);
        private readonly Mock<IFunctionTenantSource> _tenants = new(MockBehavior.Loose);
        private readonly ManualTime _time = new(RequestedAt.AddSeconds(5));
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private readonly List<string> _order = [];
        private bool _lockFree = true;

        private sealed class ManualTime(DateTime now) : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(now);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private FunctionDeletionWorker Worker(FunctionEntity? tombstone, FunctionEntity? live = null)
        {
            _pending.Add(Member);

            _db.Setup(d => d.SetMembersAsync(FunctionWorkerQueueKeys.PendingDeletions, It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => _pending.Select(m => (RedisValue)m).ToArray());
            _db.Setup(d => d.SetRemoveAsync(FunctionWorkerQueueKeys.PendingDeletions, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey _, RedisValue m, CommandFlags __) => Task.FromResult(_pending.Remove(m.ToString())));
            _db.Setup(d => d.SetAddAsync(FunctionWorkerQueueKeys.PendingDeletions, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey _, RedisValue m, CommandFlags __) => Task.FromResult(_pending.Add(m.ToString())));
            _db.Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => _lockFree);
            _db.Setup(d => d.LockReleaseAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            _db.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                    It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            _functions.Setup(f => f.GetDeletedAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => tombstone);
            _functions.Setup(f => f.GetByIdAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(live);
            _functions.Setup(f => f.RecordPurgePassAsync(Tenant, FunctionId, It.IsAny<FunctionPurgeReport>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string __, FunctionPurgeReport ___, DateTime at, CancellationToken ____) =>
                {
                    _order.Add("record-pass");
                    if (tombstone?.Deletion is { } d)
                    {
                        d.Passes++;
                        d.LastPassAt = at;
                    }
                })
                .Returns(Task.CompletedTask);
            _functions.Setup(f => f.DeleteTombstoneAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("delete-document"))
                .ReturnsAsync(true);

            _purge.Setup(p => p.PurgeAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("purge"))
                .ReturnsAsync(new FunctionPurgeReport(1, 2, 3, 4, 5, 6));

            _audit.Setup(a => a.RecordAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("audit"))
                .Returns(Task.CompletedTask);

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_db.Object);

            return new FunctionDeletionWorker(
                cache.Object, _functions.Object, _purge.Object, _audit.Object, _tenants.Object,
                new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
                NullLogger<FunctionDeletionWorker>.Instance, _time);
        }

        private static FunctionEntity Tombstone() => new()
        {
            ItemId = FunctionId,
            Deletion = new FunctionDeletion { RequestedAt = RequestedAt, RequestedBy = "u1", RequestedByEmail = "u@example.com" },
        };

        [Fact]
        public async Task The_first_pass_purges_straight_away_but_keeps_the_tombstone()
        {
            var worker = Worker(Tombstone());

            var finished = await worker.ProcessPendingAsync(CancellationToken.None);

            finished.Should().Be(0);
            _order.Should().Equal("purge", "record-pass");
            _pending.Should().Contain(Member, "the delete is not finished until the settle window has passed");
        }

        [Fact]
        public async Task Passes_are_spaced_out_while_the_delete_settles()
        {
            var worker = Worker(Tombstone());
            await worker.ProcessPendingAsync(CancellationToken.None);

            _time.Now += TimeSpan.FromSeconds(10);
            await worker.ProcessPendingAsync(CancellationToken.None);
            _order.Count(o => o == "purge").Should().Be(1);

            _time.Now += worker.PassInterval;
            await worker.ProcessPendingAsync(CancellationToken.None);
            _order.Count(o => o == "purge").Should().Be(2);
        }

        [Fact]
        public async Task After_the_settle_window_a_final_pass_removes_the_document_and_audits_it()
        {
            // A deploy that read the function just before its tombstone can take the whole build
            // wait before it writes a version and pins an image; only a pass after that catches it.
            var worker = Worker(Tombstone());
            _time.Now = new DateTimeOffset(RequestedAt) + worker.SettleWindow;

            var finished = await worker.ProcessPendingAsync(CancellationToken.None);

            finished.Should().Be(1);
            _order.Should().Equal("purge", "record-pass", "delete-document", "audit");
            _pending.Should().BeEmpty();
            _audit.Verify(a => a.RecordAsync(
                Tenant, FunctionId, FunctionsConstants.AuditActions.Purged, "u1", "u@example.com",
                It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task The_settle_window_cannot_be_configured_below_the_build_wait()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_db.Object);
            var worker = new FunctionDeletionWorker(
                cache.Object, _functions.Object, _purge.Object, _audit.Object, _tenants.Object,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Functions:DeletionSettleSeconds"] = "1",
                    ["Functions:BuildWaitSeconds"] = "600",
                }).Build(),
                NullLogger<FunctionDeletionWorker>.Instance, _time);

            worker.SettleWindow.Should().BeGreaterThan(TimeSpan.FromSeconds(600));
        }

        [Fact]
        public async Task A_queued_entry_for_a_live_function_is_dropped_and_never_purged()
        {
            var worker = Worker(tombstone: null, live: new FunctionEntity { ItemId = FunctionId });

            await worker.ProcessPendingAsync(CancellationToken.None);

            _purge.Verify(p => p.PurgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _functions.Verify(f => f.DeleteTombstoneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _pending.Should().BeEmpty();
        }

        [Fact]
        public async Task An_entry_another_worker_already_finished_is_just_dropped()
        {
            var worker = Worker(tombstone: null, live: null);

            await worker.ProcessPendingAsync(CancellationToken.None);

            _purge.Verify(p => p.PurgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _pending.Should().BeEmpty();
        }

        [Fact]
        public async Task A_function_another_worker_is_mid_pass_on_is_left_alone()
        {
            var worker = Worker(Tombstone());
            _lockFree = false;

            await worker.ProcessPendingAsync(CancellationToken.None);

            _order.Should().BeEmpty();
            _pending.Should().Contain(Member);
        }

        [Fact]
        public async Task A_malformed_entry_is_dropped_without_purging_anything()
        {
            var worker = Worker(Tombstone());
            _pending.Clear();
            _pending.Add("no-separator");
            _pending.Add("a|b|c");

            await worker.ProcessPendingAsync(CancellationToken.None);

            _pending.Should().BeEmpty();
            _purge.Verify(p => p.PurgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_failing_pass_keeps_the_entry_and_backs_off_before_trying_again()
        {
            var worker = Worker(Tombstone());
            _purge.Setup(p => p.PurgeAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .Callback(() => _order.Add("purge"))
                .ThrowsAsync(new TimeoutException("mongo"));

            await worker.ProcessPendingAsync(CancellationToken.None);
            await worker.ProcessPendingAsync(CancellationToken.None);

            _order.Should().Equal("purge");
            _pending.Should().Contain(Member);
            _functions.Verify(f => f.DeleteTombstoneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            _time.Now += TimeSpan.FromMinutes(1);
            await worker.ProcessPendingAsync(CancellationToken.None);
            _order.Should().Equal("purge", "purge");
        }

        [Fact]
        public async Task A_failing_final_pass_does_not_remove_the_document()
        {
            // The document is the only thing left that names the rest; it goes last or not at all.
            var worker = Worker(Tombstone());
            _time.Now = new DateTimeOffset(RequestedAt) + worker.SettleWindow;
            _purge.Setup(p => p.PurgeAsync(Tenant, FunctionId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            await worker.ProcessPendingAsync(CancellationToken.None);

            _functions.Verify(f => f.DeleteTombstoneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _pending.Should().Contain(Member);
        }

        [Fact]
        public async Task The_sweep_requeues_tombstones_the_redis_set_lost()
        {
            var worker = Worker(Tombstone());
            _pending.Clear();
            _tenants.Setup(t => t.GetActiveTenantIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(["t1", "t2"]);
            _functions.Setup(f => f.GetDeletedIdsAsync("t1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([FunctionId]);
            _functions.Setup(f => f.GetDeletedIdsAsync("t2", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("tenant db down"));

            var found = await worker.SweepTombstonesAsync(CancellationToken.None);

            // One tenant's failure does not stop the rest.
            found.Should().Be(1);
            _pending.Should().Equal(Member);
        }
    }
}
