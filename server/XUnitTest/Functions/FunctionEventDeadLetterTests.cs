using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The stream of events no function ran (<c>functions:dead-function-events</c>): its fields, that it never holds the
    /// payload, that only event runs reach it, and the logic-owned store of an event run's name and depth.
    /// </summary>
    public class FunctionEventDeadLetterTests
    {
        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly Dictionary<string, string> _store = [];

        private ICacheClient Cache()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return cache.Object;
        }

        private FunctionEventRunStore RunStore()
        {
            _redis.Fake.On("StringSetAsync", args => { _store[(string)(RedisKey)args[0]!] = args[1]!.ToString()!; return true; });
            _redis.Fake.On("StringGetAsync", args => _store.TryGetValue((string)(RedisKey)args[0]!, out var v) ? (RedisValue)v : RedisValue.Null);
            return new FunctionEventRunStore(Cache(), NullLogger<FunctionEventRunStore>.Instance);
        }

        private FunctionEventDeadLetters DeadLetters(IFunctionEventRunStore? store = null) =>
            new(Cache(), store ?? RunStore(), NullLogger<FunctionEventDeadLetters>.Instance);

        private Dictionary<string, string> Entry()
        {
            var add = _redis.Fake.Calls("StreamAddAsync").Should().ContainSingle().Subject;
            ((string)(RedisKey)add[0]!).Should().Be(FunctionQueueKeys.DeadFunctionEventsStream);
            return ((NameValueEntry[])add[1]!).ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        }

        [Fact]
        public async Task An_entry_has_exactly_the_agreed_fields_and_no_payload()
        {
            await DeadLetters().AddAsync("evt-1", "fn-a", "run-9", "tenant-1", "order.created", "DEPTH_LIMIT");

            Entry().Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["eventId"] = "evt-1", ["functionId"] = "fn-a", ["runId"] = "run-9",
                ["tenantId"] = "tenant-1", ["name"] = "order.created", ["reason"] = "DEPTH_LIMIT",
            });
        }

        [Fact]
        public async Task A_redis_failure_is_swallowed_so_the_consumer_never_throws()
        {
            _redis.Fake.On("StreamAddAsync", _ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var act = () => DeadLetters().AddAsync("evt-1", "fn-a", "run-9", "tenant-1", "order.created", "INVOKE_FAILED");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task A_failed_event_run_is_dead_lettered_with_its_event_id_name_and_the_failure_as_reason()
        {
            var store = RunStore();
            await store.SaveAsync("run-9", new FunctionEventRunInfo("evt-1", "order.created", 2));
            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
            var run = new FunctionRunEntity
            {
                ItemId = "run-9", FunctionId = "fn-a", InvokedBy = InvokedByType.Event, InvokedById = "evt-1",
                Status = RunStatus.Failed, ErrorCode = RunErrorCode.UserRuntimeError, ErrorMessage = "boom: secret payload text",
            };

            await DeadLetters(store).RunFailedAsync("tenant-1", run);

            var entry = Entry();
            entry["eventId"].Should().Be("evt-1");
            entry["functionId"].Should().Be("fn-a");
            entry["runId"].Should().Be("run-9");
            entry["name"].Should().Be("order.created");
            entry["reason"].Should().Be("RUN_FAILED:Failed/UserRuntimeError");
            entry.Values.Should().NotContain(v => v.Contains("secret payload"), "the error text is never copied");
        }

        [Fact]
        public async Task An_expired_event_run_has_the_EVENT_EXPIRED_reason()
        {
            var run = new FunctionRunEntity
            {
                ItemId = "run-9", FunctionId = "fn-a", InvokedBy = InvokedByType.Event, InvokedById = "evt-1",
                Status = RunStatus.Failed, ErrorCode = RunErrorCode.Undeliverable, ErrorMessage = "EVENT_EXPIRED: the event run waited more than 300 s",
            };

            await DeadLetters().RunFailedAsync("tenant-1", run);

            Entry()["reason"].Should().Be("EVENT_EXPIRED");
        }

        [Theory]
        [InlineData(InvokedByType.Http)]
        [InlineData(InvokedByType.Workflow)]
        [InlineData(InvokedByType.Test)]
        [InlineData(InvokedByType.Replay)]
        public async Task A_run_no_event_started_never_reaches_the_event_dead_letters(InvokedByType invokedBy)
        {
            await DeadLetters().RunFailedAsync("tenant-1", new FunctionRunEntity { ItemId = "r", InvokedBy = invokedBy, Status = RunStatus.Failed });

            _redis.Fake.Calls("StreamAddAsync").Should().BeEmpty();
        }

        [Fact]
        public async Task The_store_keeps_the_event_info_under_the_runs_key_with_a_ttl_longer_than_the_runs_payload()
        {
            var store = RunStore();

            await store.SaveAsync("run-9", new FunctionEventRunInfo("evt-1", "order.created", 4));

            (await store.GetAsync("run-9")).Should().Be(new FunctionEventRunInfo("evt-1", "order.created", 4));
            _store.Keys.Should().ContainSingle().Which.Should().Be(FunctionWorkerQueueKeys.EventRunInfo("run-9"));
            FunctionWorkerQueueKeys.EventRunInfoTtl.Should().BeGreaterThan(FunctionQueueKeys.RunTtl);
        }

        [Fact]
        public async Task A_missing_or_unreadable_value_reads_as_unknown_not_as_an_error()
        {
            var store = RunStore();
            (await store.GetAsync("nobody")).Should().BeNull();

            _store[FunctionWorkerQueueKeys.EventRunInfo("bad")] = "{not json";
            (await store.GetAsync("bad")).Should().BeNull();

            _redis.Fake.On("StringGetAsync", _ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
            (await store.GetAsync("run-9")).Should().BeNull();
        }

        [Fact]
        public async Task A_failed_write_surfaces_so_no_run_is_created_without_its_depth()
        {
            _redis.Fake.On("StringSetAsync", _ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
            var store = new FunctionEventRunStore(Cache(), NullLogger<FunctionEventRunStore>.Instance);

            var act = () => store.SaveAsync("run-9", new FunctionEventRunInfo("evt-1", "order.created", 0));

            await act.Should().ThrowAsync<RedisConnectionException>();
        }
    }
}
