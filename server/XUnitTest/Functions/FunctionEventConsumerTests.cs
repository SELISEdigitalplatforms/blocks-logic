using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The event trigger's receiving side: validation, tenant from the message's context, the depth limit, who the
    /// subscribers are, one start per subscriber (key = event + function), retry with backoff then dead letter, and
    /// that nothing is ever thrown out of <c>Consume</c>.
    /// </summary>
    public class FunctionEventConsumerTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IFunctionEventSettingsRepository> _settings = new();
        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionInvocationService> _invocation = new();
        private readonly Mock<IFunctionEventDeadLetters> _dead = new();
        private readonly List<TimeSpan> _pauses = [];
        private readonly List<(string FunctionId, FunctionEventInvocation Event, string? Tenant, string? ContextTenant)> _started = [];
        private readonly Dictionary<string, FunctionEntity> _live = [];

        public FunctionEventConsumerTests()
        {
            SetContext(Tenant);
            _settings.Setup(s => s.FindSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => [.. _live.Values.Select(f => new FunctionEventSubscription(f.ItemId, f.ActiveVersionId!))]);
            _functions.Setup(f => f.GetLiveByIdsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, IReadOnlyCollection<string> ids, CancellationToken _) => [.. _live.Values.Where(f => ids.Contains(f.ItemId))]);
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .Callback((string tenant, string fn, FunctionEventInvocation e, BlocksContext? c, CancellationToken _) => _started.Add((fn, e, tenant, c?.TenantId)))
                .ReturnsAsync(new InvokeResultDto { RunId = "run-1", Status = "QUEUED" });
        }

        public void Dispose() => BlocksContext.SetContext(null, false);

        private static void SetContext(string? tenant) => BlocksContext.SetContext(
            tenant is null ? null : BlocksContext.Create(tenant, [], string.Empty, false, string.Empty, string.Empty, DateTime.MinValue, string.Empty, [], string.Empty, string.Empty, string.Empty, string.Empty, tenant), tenant is not null);

        private FunctionEventConsumer Consumer() => new(
            _settings.Object, _functions.Object, _invocation.Object, _dead.Object, NullLogger<FunctionEventConsumer>.Instance)
        {
            Pause = delay => { _pauses.Add(delay); return Task.CompletedTask; },
        };

        private void Live(string id, string versionId = "v") =>
            _live[id] = new FunctionEntity { ItemId = id, Status = FunctionStatus.Live, ActiveVersionId = versionId };

        private static FunctionEvent Event(string id = "run-0-1-1", string name = "order.created", string data = """{"a":1}""", int depth = 0, string source = "fn-src") => new()
        {
            EventId = id, Name = name, DataJson = data, Depth = depth, SourceFunctionId = source, SourceRunId = "run-0",
        };

        private void DeadLettered(string reason, Times times) => _dead.Verify(d => d.AddAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(r => r.StartsWith(reason, StringComparison.Ordinal))), times);

        // ---------------------------------------------------------------- subscribers ----

        [Fact]
        public async Task Every_subscriber_is_started_with_the_event_and_the_message_context()
        {
            Live("fn-a");
            Live("fn-b");

            await Consumer().Consume(Event());

            _started.Select(s => s.FunctionId).Should().Equal("fn-a", "fn-b");
            _started.Should().OnlyContain(s => s.Tenant == Tenant && s.ContextTenant == Tenant);
            _started[0].Event.Should().Be(new FunctionEventInvocation("run-0-1-1", "order.created", """{"a":1}""", 0));
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task The_tenant_is_the_message_context_never_a_payload_field()
        {
            SetContext("tenant-from-context");
            Live("fn-a");

            await Consumer().Consume(Event(data: """{"tenantId":"tenant-evil"}"""));

            _started.Should().ContainSingle().Which.Tenant.Should().Be("tenant-from-context");
            _settings.Verify(s => s.FindSubscriptionsAsync("tenant-from-context", "order.created", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task The_sender_is_never_started_by_its_own_event()
        {
            Live("fn-src");
            Live("fn-other");

            await Consumer().Consume(Event(source: "fn-src"));

            _started.Select(s => s.FunctionId).Should().Equal("fn-other");
        }

        [Fact]
        public async Task Only_the_first_ten_by_function_id_are_started_and_the_rest_are_skipped()
        {
            foreach (var n in Enumerable.Range(1, 13).Reverse()) Live($"fn-{n:00}");

            await Consumer().Consume(Event());

            _started.Select(s => s.FunctionId).Should().Equal(Enumerable.Range(1, 10).Select(n => $"fn-{n:00}"));
        }

        [Fact]
        public async Task A_function_whose_active_version_is_not_the_subscribed_one_is_not_started()
        {
            // fn-a is subscribed in v-old only; its active version is v-new (a redeploy dropped the subscription).
            _settings.Setup(s => s.FindSubscriptionsAsync(Tenant, "order.created", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new FunctionEventSubscription("fn-a", "v-old"), new FunctionEventSubscription("fn-b", "v-1")]);
            Live("fn-a", "v-new");
            Live("fn-b", "v-1");

            await Consumer().Consume(Event());

            _started.Select(s => s.FunctionId).Should().Equal("fn-b");
        }

        [Fact]
        public async Task A_function_that_is_not_live_is_not_started()
        {
            // The lookup returns only Live functions: a paused or deleted one is simply absent.
            _settings.Setup(s => s.FindSubscriptionsAsync(Tenant, "order.created", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new FunctionEventSubscription("fn-paused", "v-1")]);

            await Consumer().Consume(Event());

            _started.Should().BeEmpty();
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task An_event_nobody_subscribes_to_is_not_an_error()
        {
            await Consumer().Consume(Event());

            _started.Should().BeEmpty();
            _dead.VerifyNoOtherCalls();
        }

        // ----------------------------------------------------------------- validation ----

        [Theory]
        [InlineData("", "order.created", """{"a":1}""")]
        [InlineData("run-0-1-1", "NO", """{"a":1}""")]
        [InlineData("run-0-1-1", "Order.Created", """{"a":1}""")]
        [InlineData("run-0-1-1", "order.created", "{not json")]
        [InlineData("run-0-1-1", "order.created", "")]
        public async Task An_invalid_event_is_dead_lettered_and_starts_nothing(string id, string name, string data)
        {
            Live("fn-a");

            await Consumer().Consume(Event(id, name, data));

            _started.Should().BeEmpty();
            DeadLettered("INVALID_MESSAGE", Times.Once());
        }

        [Fact]
        public async Task Data_over_64_KB_is_invalid()
        {
            Live("fn-a");
            var big = "\"" + new string('x', 64 * 1024) + "\"";

            await Consumer().Consume(Event(data: big));

            _started.Should().BeEmpty();
            DeadLettered("INVALID_MESSAGE", Times.Once());
        }

        [Fact]
        public async Task Data_of_exactly_64_KB_is_accepted()
        {
            Live("fn-a");
            var data = "\"" + new string('x', 64 * 1024 - 2) + "\"";

            await Consumer().Consume(Event(data: data));

            _started.Should().ContainSingle();
        }

        [Fact]
        public async Task A_bad_name_is_not_echoed_into_the_dead_letter_entry()
        {
            await Consumer().Consume(Event(name: "<script>x"));

            _dead.Verify(d => d.AddAsync("run-0-1-1", string.Empty, string.Empty, Tenant, string.Empty, "INVALID_MESSAGE"), Times.Once);
        }

        [Fact]
        public async Task A_message_without_a_tenant_in_its_context_is_dropped_and_dead_lettered()
        {
            SetContext(null);
            Live("fn-a");

            await Consumer().Consume(Event());

            _started.Should().BeEmpty();
            DeadLettered("NO_TENANT", Times.Once());
        }

        [Fact]
        public async Task An_empty_message_is_dead_lettered()
        {
            await Consumer().Consume(null!);

            DeadLettered("INVALID_MESSAGE", Times.Once());
        }

        // ---------------------------------------------------------------------- depth ----

        [Fact]
        public async Task Depth_five_runs_and_depth_six_is_refused_with_the_depth_reason()
        {
            Live("fn-a");

            await Consumer().Consume(Event(depth: 5));
            _started.Should().ContainSingle();

            await Consumer().Consume(Event(id: "run-0-1-2", depth: 6));

            _started.Should().ContainSingle("depth 6 starts nothing");
            _dead.Verify(d => d.AddAsync("run-0-1-2", string.Empty, string.Empty, Tenant, "order.created", "DEPTH_LIMIT"), Times.Once);
        }

        [Fact]
        public async Task A_negative_depth_is_invalid()
        {
            await Consumer().Consume(Event(depth: -1));

            DeadLettered("INVALID_MESSAGE", Times.Once());
        }

        // ------------------------------------------------------------ retry / dead letter ----

        [Fact]
        public async Task A_start_that_fails_is_retried_with_short_pauses_and_the_third_try_can_succeed()
        {
            Live("fn-a");
            var calls = 0;
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ++calls < 3 ? throw new FunctionRateLimitedException("busy", 1) : Task.FromResult(new InvokeResultDto { RunId = "r" }));

            await Consumer().Consume(Event());

            calls.Should().Be(3);
            _pauses.Should().HaveCount(2).And.OnlyContain(p => p > TimeSpan.Zero && p <= TimeSpan.FromSeconds(2));
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task After_three_failed_tries_the_event_is_dead_lettered_for_that_function_only()
        {
            Live("fn-a");
            Live("fn-b");
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionUnavailableException("queue down", "run-x"));

            await Consumer().Consume(Event());

            _invocation.Verify(i => i.InvokeFromEventAsync(Tenant, "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            _dead.Verify(d => d.AddAsync("run-0-1-1", "fn-a", string.Empty, Tenant, "order.created", "INVOKE_FAILED:FunctionUnavailableException"), Times.Once);
            _started.Select(s => s.FunctionId).Should().Equal("fn-b");
        }

        [Fact]
        public async Task A_function_that_stopped_subscribing_in_the_meantime_is_skipped_without_retry_or_dead_letter()
        {
            Live("fn-a");
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionAuthorizationException("does not subscribe"));

            await Consumer().Consume(Event());

            _invocation.Verify(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()), Times.Once);
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_lookup_that_keeps_failing_is_dead_lettered_and_never_thrown()
        {
            _settings.Setup(s => s.FindSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = () => Consumer().Consume(Event());

            await act.Should().NotThrowAsync();
            _settings.Verify(s => s.FindSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            DeadLettered("INVOKE_FAILED:TimeoutException", Times.Once());
        }

        [Fact]
        public async Task The_same_event_delivered_again_asks_for_the_same_run_key_so_the_invocation_can_dedupe()
        {
            Live("fn-a");

            await Consumer().Consume(Event());
            await Consumer().Consume(Event());

            _started.Should().HaveCount(2);
            _started.Select(s => s.Event.EventId).Distinct().Should().ContainSingle().Which.Should().Be("run-0-1-1");
        }
    }
}
