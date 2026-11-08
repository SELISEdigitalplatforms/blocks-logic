using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Actions;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The Action trigger for functions: the Function switch re-checked, tenant from the message's context, every Live
    /// subscriber started (no cap, paged lookup), one start per subscriber (key = event + function), retry with backoff
    /// then dead letter, and that nothing is ever thrown.
    /// </summary>
    public sealed class FunctionActionTriggerTests : IDisposable
    {
        private const string Tenant = "tenant-1";
        private const string ActionName = "iam.user.created";
        private const string EventId = "evt_0123456789abcdef0123456789abcdef";

        private readonly Mock<IFunctionEventSettingsRepository> _settings = new();
        private readonly Mock<IFunctionRepository> _functions = new();
        private readonly Mock<IFunctionInvocationService> _invocation = new();
        private readonly Mock<IFunctionEventDeadLetters> _dead = new();
        private readonly Mock<IActionsRepository> _actions = new();
        private readonly List<TimeSpan> _pauses = [];
        private readonly List<(string FunctionId, FunctionEventInvocation Event, string? Tenant, string? ContextTenant)> _started = [];
        private readonly SortedDictionary<string, FunctionEntity> _live = new(StringComparer.Ordinal);

        public FunctionActionTriggerTests()
        {
            SetContext(Tenant);
            Switch(function: true);
            // A real keyset page over the live functions' subscriptions, in (FunctionId, VersionId) order.
            _settings.Setup(s => s.FindSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FunctionEventSubscription?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string _, FunctionEventSubscription? after, int limit, CancellationToken _) =>
                    [.. _live.Values
                        .Select(f => new FunctionEventSubscription(f.ItemId, f.ActiveVersionId!))
                        .Where(s => after is null || string.CompareOrdinal(s.FunctionId, after.FunctionId) > 0)
                        .Take(limit)]);
            _functions.Setup(f => f.GetLiveByIdsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, IReadOnlyCollection<string> ids, CancellationToken _) => [.. _live.Values.Where(f => ids.Contains(f.ItemId))]);
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .Callback((string tenant, string fn, FunctionEventInvocation e, BlocksContext? c, CancellationToken _) => _started.Add((fn, e, tenant, c?.TenantId)))
                .ReturnsAsync(new InvokeResultDto { RunId = "run-1", Status = "QUEUED" });
        }

        public void Dispose() => BlocksContext.SetContext(null, false);

        private static void SetContext(string? tenant) => BlocksContext.SetContext(
            tenant is null ? null : BlocksContext.Create(tenant, [], string.Empty, false, string.Empty, string.Empty, DateTime.MinValue, string.Empty, [], string.Empty, string.Empty, string.Empty, string.Empty, tenant), tenant is not null);

        private void Switch(bool function, bool workflow = false) =>
            _actions.Setup(a => a.FindAsync(ActionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActionDefinition { Name = ActionName, Function = function, Workflow = workflow });

        private FunctionActionTriggerService Trigger() => new(
            _settings.Object, _functions.Object, _invocation.Object, _dead.Object, _actions.Object, NullLogger<FunctionActionTriggerService>.Instance)
        {
            Pause = delay => { _pauses.Add(delay); return Task.CompletedTask; },
        };

        private void Live(string id, string versionId = "v") =>
            _live[id] = new FunctionEntity { ItemId = id, Status = FunctionStatus.Live, ActiveVersionId = versionId };

        private static BlocksAction Action(string id = EventId, string name = ActionName, string data = """{"userId":"u-1"}""") => new()
        {
            Id = id,
            Action = name,
            Time = new DateTime(2026, 11, 4, 9, 20, 0, DateTimeKind.Utc),
            OrganizationId = null,
            DataJson = data,
        };

        private void DeadLettered(string reason, Times times) => _dead.Verify(d => d.AddAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(r => r.StartsWith(reason, StringComparison.Ordinal))), times);

        // ---------------------------------------------------------------- subscribers ----

        [Fact]
        public async Task Every_subscriber_is_started_with_the_simple_action_json_and_the_message_context()
        {
            Live("fn-a");
            Live("fn-b");

            await Trigger().StartAsync(Action());

            _started.Select(s => s.FunctionId).Should().Equal("fn-a", "fn-b");
            _started.Should().OnlyContain(s => s.Tenant == Tenant && s.ContextTenant == Tenant);
            var invocation = _started[0].Event;
            invocation.EventId.Should().Be(EventId);
            invocation.Name.Should().Be(ActionName);
            using var input = JsonDocument.Parse(invocation.DataJson);
            input.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("id", "action", "time", "organizationId", "data");
            input.RootElement.GetProperty("time").GetString().Should().Be("2026-11-04T09:20:00.000Z");
            input.RootElement.GetProperty("organizationId").ValueKind.Should().Be(JsonValueKind.Null);
            input.RootElement.GetProperty("data").GetProperty("userId").GetString().Should().Be("u-1");
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task More_than_ten_subscribers_all_run_across_several_lookup_pages()
        {
            // 250 subscribers = 3 pages of 100: no cap, and the lookup never loads them all at once.
            foreach (var n in Enumerable.Range(1, 250)) Live($"fn-{n:000}");

            await Trigger().StartAsync(Action());

            _started.Select(s => s.FunctionId).Should().Equal(Enumerable.Range(1, 250).Select(n => $"fn-{n:000}"));
            _settings.Verify(s => s.FindSubscriptionsAsync(Tenant, ActionName, It.IsAny<FunctionEventSubscription?>(), FunctionActionTriggerService.SubscriptionPageSize, It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task The_tenant_is_the_message_context_never_a_data_field()
        {
            SetContext("tenant-from-context");
            Live("fn-a");

            await Trigger().StartAsync(Action(data: """{"tenantId":"tenant-evil"}"""));

            _started.Should().ContainSingle().Which.Tenant.Should().Be("tenant-from-context");
        }

        [Fact]
        public async Task A_function_whose_active_version_is_not_the_subscribed_one_is_not_started()
        {
            // fn-a is subscribed in v-old only; its active version is v-new (a redeploy dropped the subscription).
            _settings.Setup(s => s.FindSubscriptionsAsync(Tenant, ActionName, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new FunctionEventSubscription("fn-a", "v-old"), new FunctionEventSubscription("fn-b", "v-1")]);
            Live("fn-a", "v-new");
            Live("fn-b", "v-1");

            await Trigger().StartAsync(Action());

            _started.Select(s => s.FunctionId).Should().Equal("fn-b");
        }

        [Fact]
        public async Task An_action_nobody_subscribes_to_is_not_an_error()
        {
            await Trigger().StartAsync(Action());

            _started.Should().BeEmpty();
            _dead.VerifyNoOtherCalls();
        }

        // ------------------------------------------------------------------- switch ----

        [Fact]
        public async Task A_function_switch_turned_off_after_sending_starts_nothing()
        {
            Live("fn-a");
            Switch(function: false, workflow: true);

            await Trigger().StartAsync(Action());

            _started.Should().BeEmpty();
            _settings.Verify(s => s.FindSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FunctionEventSubscription?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task An_action_missing_from_the_tenant_list_starts_nothing()
        {
            Live("fn-a");
            _actions.Setup(a => a.FindAsync(ActionName, It.IsAny<CancellationToken>())).ReturnsAsync((ActionDefinition?)null);

            await Trigger().StartAsync(Action());

            _started.Should().BeEmpty();
        }

        // ----------------------------------------------------------------- validation ----

        [Theory]
        [InlineData("", ActionName)]
        [InlineData(EventId, "NO")]
        [InlineData(EventId, "Iam.User.Created")]
        public async Task An_invalid_action_is_dead_lettered_and_starts_nothing(string id, string name)
        {
            Live("fn-a");

            await Trigger().StartAsync(Action(id, name));

            _started.Should().BeEmpty();
            DeadLettered("INVALID_MESSAGE", Times.Once());
        }

        [Fact]
        public async Task Data_over_64_KB_is_invalid()
        {
            Live("fn-a");
            var big = """{"x":""" + "\"" + new string('x', 64 * 1024) + "\"}";

            await Trigger().StartAsync(Action(data: big));

            _started.Should().BeEmpty();
            DeadLettered("INVALID_MESSAGE", Times.Once());
        }

        [Fact]
        public async Task A_message_without_a_tenant_in_its_context_is_dropped_and_dead_lettered()
        {
            SetContext(null);
            Live("fn-a");

            await Trigger().StartAsync(Action());

            _started.Should().BeEmpty();
            DeadLettered("NO_TENANT", Times.Once());
        }

        // ------------------------------------------------------------ retry / dead letter ----

        [Fact]
        public async Task A_start_that_fails_is_retried_with_short_pauses_and_the_third_try_can_succeed()
        {
            Live("fn-a");
            var calls = 0;
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .Returns(() => ++calls < 3 ? throw new FunctionRateLimitedException("busy", 1) : Task.FromResult(new InvokeResultDto { RunId = "r" }));

            await Trigger().StartAsync(Action());

            calls.Should().Be(3);
            _pauses.Should().HaveCount(2).And.OnlyContain(p => p > TimeSpan.Zero && p <= TimeSpan.FromSeconds(2));
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task After_three_failed_tries_the_action_is_dead_lettered_for_that_function_only()
        {
            Live("fn-a");
            Live("fn-b");
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionUnavailableException("queue down", "run-x"));

            await Trigger().StartAsync(Action());

            _dead.Verify(d => d.AddAsync(EventId, "fn-a", string.Empty, Tenant, ActionName, "INVOKE_FAILED:FunctionUnavailableException"), Times.Once);
            _started.Select(s => s.FunctionId).Should().Equal("fn-b");
        }

        [Fact]
        public async Task A_function_that_stopped_subscribing_in_the_meantime_is_skipped_without_retry_or_dead_letter()
        {
            Live("fn-a");
            _invocation.Setup(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FunctionAuthorizationException("does not subscribe"));

            await Trigger().StartAsync(Action());

            _invocation.Verify(i => i.InvokeFromEventAsync(It.IsAny<string>(), "fn-a", It.IsAny<FunctionEventInvocation>(), It.IsAny<BlocksContext?>(), It.IsAny<CancellationToken>()), Times.Once);
            _dead.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_lookup_that_keeps_failing_is_dead_lettered_and_never_thrown()
        {
            _settings.Setup(s => s.FindSubscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FunctionEventSubscription?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo"));

            var act = () => Trigger().StartAsync(Action());

            await act.Should().NotThrowAsync();
            DeadLettered("INVOKE_FAILED:TimeoutException", Times.Once());
        }

        [Fact]
        public async Task The_same_event_delivered_again_asks_for_the_same_run_key_so_the_invocation_can_dedupe()
        {
            Live("fn-a");

            await Trigger().StartAsync(Action());
            await Trigger().StartAsync(Action());

            _started.Should().HaveCount(2);
            _started.Select(s => s.Event).Distinct().Should().ContainSingle().Which.EventId.Should().Be(EventId);
        }
    }
}
