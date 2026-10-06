using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The pool's promises (sandbox/REUSE.md): reuse only within one tenant + function + version +
    /// image, one call at a time, paused between calls, destroyed for every reason the contract
    /// lists, swept when idle, and never holding host memory it no longer has a sandbox for.
    /// </summary>
    public sealed class WarmPoolTests : IAsyncDisposable
    {
        private static readonly WarmKey Key = new("tenant_a", "fn_1", "v1", "img@sha256:aa");
        private readonly ManualTime _time = new();
        private readonly RunnerOptions _options;
        private readonly HostBudget _budget;
        private readonly ScriptedFactory _factory;
        private readonly WarmPool _pool;

        public WarmPoolTests() : this(null) { }

        private WarmPoolTests(Action<RunnerOptions>? configure)
        {
            _options = new RunnerOptions
            {
                MaxActiveSandboxes = 50,
                StartCostCpuMs = 0,
                WarmMaxCalls = 1000,
                WarmMaxAgeSeconds = 3600,
                WarmIdleSeconds = 600,
                RunnerId = "runner-test",
            };
            configure?.Invoke(_options);
            var options = Microsoft.Extensions.Options.Options.Create(_options);
            _budget = new HostBudget(options, new CalmHost(), new SandboxFootprint(), NullLogger<HostBudget>.Instance, _time);
            _factory = new ScriptedFactory(_options);
            _pool = new WarmPool(_factory, _budget, options, NullLogger<WarmPool>.Instance, _time);
        }

        private static WarmPoolTests With(Action<RunnerOptions> configure) => new(configure);

        public async ValueTask DisposeAsync() => await _pool.DisposeAsync();

        private async Task<WarmHandle> AcquireAsync(WarmKey? key = null)
        {
            var result = await _pool.AcquireAsync(key ?? Key, RunLimits.Default, CancellationToken.None);
            result.Status.Should().Be(WarmAcquireStatus.Acquired);
            return result.Handle!;
        }

        private static Task<WarmCallResult> CallAsync(WarmHandle handle, string id) =>
            handle.RunCallAsync(id, Lines.Envelope(id), RunLimits.Default, null, CancellationToken.None);

        private async Task<string> ServeAsync(WarmHandle handle, string id) =>
            await _pool.ReleaseAsync(handle, await CallAsync(handle, id));

        [Fact]
        public async Task A_clean_sandbox_is_paused_and_reused_by_the_next_call()
        {
            var first = await AcquireAsync();
            first.Reused.Should().BeFalse();
            first.StartupMs.Should().NotBeNull();

            (await ServeAsync(first, "run_1")).Should().BeEmpty();
            _factory.Created[0].Paused.Should().BeTrue("nothing may run between calls");
            _pool.Counts.Should().Be((1, 0, 1));

            var second = await AcquireAsync();

            second.Reused.Should().BeTrue();
            second.StartupMs.Should().BeNull();
            second.Sandbox.Should().BeSameAs(first.Sandbox);
            _factory.Created.Should().ContainSingle();
            _factory.Created[0].Paused.Should().BeTrue("it is resumed only when its call is handed over");
            _pool.Counts.Should().Be((1, 1, 0));

            await CallAsync(second, "run_2");
            _factory.Created[0].Unpauses.Should().Be(1);
        }

        [Fact]
        public async Task A_busy_sandbox_is_never_handed_to_a_second_caller()
        {
            var first = await AcquireAsync();
            var second = await AcquireAsync();

            second.Sandbox.Should().NotBeSameAs(first.Sandbox);
            _factory.Created.Should().HaveCount(2);
            _pool.Counts.Should().Be((2, 2, 0));
        }

        [Fact]
        public async Task A_version_at_its_cap_has_no_room()
        {
            await using var t = With(o => o.WarmMaxPerVersion = 1);
            await t.AcquireAsync();

            (await t._pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None)).Status
                .Should().Be(WarmAcquireStatus.NoCapacity);
        }

        [Theory]
        [InlineData("tenant_b", "fn_1", "v1", "img@sha256:aa")]
        [InlineData("tenant_a", "fn_2", "v1", "img@sha256:aa")]
        [InlineData("tenant_a", "fn_1", "v2", "img@sha256:aa")]
        [InlineData("tenant_a", "fn_1", "v1", "img@sha256:bb")]
        public async Task A_sandbox_is_never_shared_across_tenant_function_version_or_image(
            string tenant, string function, string version, string image)
        {
            var first = await AcquireAsync();
            await ServeAsync(first, "run_1");

            var other = await AcquireAsync(new WarmKey(tenant, function, version, image));

            other.Reused.Should().BeFalse();
            other.Sandbox.Should().NotBeSameAs(first.Sandbox);
        }

        [Fact]
        public async Task A_dirty_call_destroys_the_sandbox_and_frees_its_memory()
        {
            _factory.Configure = c => c.OnCall = id =>
                [Lines.Started(id), Lines.Result(id, "1"), Lines.Idle(id, clean: false, ["Timeout"])];
            var handle = await AcquireAsync();
            _budget.CommittedMemoryBytes.Should().BePositive();

            (await ServeAsync(handle, "run_1")).Should().Be("dirty:Timeout");

            _factory.Created[0].Disposed.Should().BeTrue();
            _pool.Counts.Should().Be((0, 0, 0));
            _budget.CommittedMemoryBytes.Should().Be(0);
            _budget.Warm.Should().Be(0);
        }

        [Fact]
        public async Task A_crashed_sandbox_is_destroyed_as_a_crash()
        {
            _factory.Configure = c => c.OnCall = _ => { c.End(); return []; };
            var handle = await AcquireAsync();

            (await ServeAsync(handle, "run_1")).Should().Be("crash");
            _pool.Counts.Total.Should().Be(0);
        }

        [Fact]
        public async Task A_runtime_timeout_is_destroyed_as_a_timeout()
        {
            _factory.Configure = c => c.OnCall = id =>
            [
                Lines.Started(id), Lines.Failure(id, ErrorCodes.TimedOut, "too slow"),
                Lines.Idle(id, clean: false, ["timeout"]), Lines.Fatal(ErrorCodes.TimedOut, "too slow"),
            ];
            var handle = await AcquireAsync();

            (await ServeAsync(handle, "run_1")).Should().Be("timeout");
        }

        [Fact]
        public async Task A_protocol_violation_is_destroyed_as_protocol()
        {
            _factory.Configure = c => c.OnCall = id => [Lines.Idle("not_this_call", clean: true)];
            var handle = await AcquireAsync();

            (await ServeAsync(handle, "run_1")).Should().Be("protocol");
        }

        [Fact]
        public async Task A_sandbox_that_will_not_pause_is_not_reused()
        {
            _factory.Configure = c => c.FailPause = true;
            var handle = await AcquireAsync();

            (await ServeAsync(handle, "run_1")).Should().Be("protocol");
            _pool.Counts.Total.Should().Be(0);
        }

        [Fact]
        public async Task A_sandbox_is_recycled_after_its_maximum_calls()
        {
            await using var t = With(o => o.WarmMaxCalls = 2);

            var h1 = await t.AcquireAsync();
            (await t.ServeAsync(h1, "run_1")).Should().BeEmpty();
            var h2 = await t.AcquireAsync();
            (await t.ServeAsync(h2, "run_2")).Should().Be("maxCalls");
            t._pool.Counts.Total.Should().Be(0);
        }

        [Fact]
        public async Task A_sandbox_is_recycled_after_its_maximum_age()
        {
            var handle = await AcquireAsync();
            _time.Advance(TimeSpan.FromSeconds(_options.WarmMaxAgeSeconds + 1));

            (await ServeAsync(handle, "run_1")).Should().Be("maxAge");
        }

        [Fact]
        public async Task A_sandbox_past_the_memory_high_water_mark_is_recycled()
        {
            // 120 MB of a 128 MB limit is past 90 %.
            _factory.Configure = c => c.MemoryBytes = 120L * 1024 * 1024;
            var handle = await AcquireAsync();

            (await ServeAsync(handle, "run_1")).Should().Be("memory");
        }

        [Fact]
        public async Task The_sweep_destroys_idle_sandboxes_and_leaves_busy_ones()
        {
            var idle = await AcquireAsync();
            var busy = await AcquireAsync();
            await ServeAsync(idle, "run_1");

            _time.Advance(TimeSpan.FromSeconds(_options.WarmIdleSeconds - 1));
            (await _pool.SweepAsync()).Should().Be(0);

            _time.Advance(TimeSpan.FromSeconds(2));
            (await _pool.SweepAsync()).Should().Be(1);

            _pool.Counts.Should().Be((1, 1, 0));
            busy.Sandbox.IsDead.Should().BeFalse();
            idle.Sandbox.IsDead.Should().BeTrue();
        }

        [Fact]
        public async Task An_image_without_reuse_runtime_is_remembered_and_never_started_again()
        {
            _factory.Configure = c =>
            {
                c.OnStart.Clear();
                c.OnStart.Add(Lines.V1NoEnvelope());
                c.Exit = (20, false);
            };

            (await _pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None)).Status
                .Should().Be(WarmAcquireStatus.NoReuseSupport);
            _pool.SupportsReuse(Key.Image).Should().BeFalse();

            (await _pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None)).Status
                .Should().Be(WarmAcquireStatus.NoReuseSupport);
            _factory.Created.Should().ContainSingle("the answer is cached per image");
            _budget.CommittedMemoryBytes.Should().Be(0);
        }

        [Fact]
        public async Task A_function_that_fails_to_load_is_a_failed_start_not_a_missing_runtime()
        {
            _factory.Configure = c =>
            {
                c.OnStart.Clear();
                c.OnStart.Add(Lines.Fatal(ErrorCodes.UserRuntimeError, "the function module failed to load: x"));
            };

            var result = await _pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None);

            result.Status.Should().Be(WarmAcquireStatus.StartFailed);
            result.Start!.Status.Should().Be(WarmStartStatus.LoadFailed);
            _pool.SupportsReuse(Key.Image).Should().BeTrue();
        }

        [Fact]
        public async Task No_memory_means_no_new_sandbox_after_evicting_other_idle_ones()
        {
            var host = new CalmHost { TotalMemoryBytes = (2048L + 128) * 1024 * 1024 };
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions { StartCostCpuMs = 0, MaxActiveSandboxes = 10 });
            var budget = new HostBudget(options, host, new SandboxFootprint(), NullLogger<HostBudget>.Instance, _time);
            var factory = new ScriptedFactory(options.Value);
            await using var pool = new WarmPool(factory, budget, options, NullLogger<WarmPool>.Instance, _time);

            var other = (await pool.AcquireAsync(Key with { FunctionId = "fn_other" }, RunLimits.Default, default)).Handle!;
            await pool.ReleaseAsync(other, await CallAsync(other, "run_0"));

            // Room for one sandbox: the idle one of another function is evicted for this one.
            var mine = await pool.AcquireAsync(Key, RunLimits.Default, default);
            mine.Status.Should().Be(WarmAcquireStatus.Acquired);
            other.Sandbox.IsDead.Should().BeTrue();

            // Busy, it cannot be evicted: no room for a second.
            (await pool.AcquireAsync(Key with { FunctionId = "fn_third" }, RunLimits.Default, default)).Status
                .Should().Be(WarmAcquireStatus.NoCapacity);
        }

        [Fact]
        public async Task The_start_rate_limit_applies_to_warm_starts()
        {
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                StartCostCpuMs = 800, StartsPerSecondPerCore = 1, MaxActiveSandboxes = 10,
            });
            var budget = new HostBudget(options, new CalmHost { Cores = 1 }, new SandboxFootprint(), NullLogger<HostBudget>.Instance, _time);
            await using var pool = new WarmPool(new ScriptedFactory(options.Value), budget, options, NullLogger<WarmPool>.Instance, _time);

            (await pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.Acquired);
            (await pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.NoCapacity);

            _time.Advance(TimeSpan.FromSeconds(1));
            (await pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.Acquired);
        }

        [Fact]
        public async Task Prewarm_starts_paused_sandboxes_whose_first_call_counts_as_reused()
        {
            (await _pool.PrewarmAsync(Key, RunLimits.Default, 2, default)).Should().Be(2);
            _pool.Counts.Should().Be((2, 0, 2));
            _factory.Created.Should().OnlyContain(c => c.Paused);

            var handle = await AcquireAsync();
            handle.Reused.Should().BeTrue();
            handle.StartupMs.Should().BeNull();
        }

        [Fact]
        public async Task Prewarm_stops_at_the_version_cap()
        {
            await using var t = With(o => o.WarmMaxPerVersion = 3);
            (await t._pool.PrewarmAsync(Key, RunLimits.Default, 10, default)).Should().Be(3);
        }

        [Fact]
        public async Task Drain_destroys_idle_sandboxes_now_and_busy_ones_after_their_call()
        {
            var idle = await AcquireAsync();
            await ServeAsync(idle, "run_1");
            var busy = await AcquireAsync(Key);   // idle one is reused, so take a second
            busy.Sandbox.Should().BeSameAs(idle.Sandbox);
            var second = await AcquireAsync(Key);
            await ServeAsync(second, "run_2");
            var unrelated = await AcquireAsync(Key with { VersionId = "v2" });
            await ServeAsync(unrelated, "run_3");

            (await _pool.DrainAsync(Key.TenantId, Key.FunctionId, Key.VersionId)).Should().Be(1);
            second.Sandbox.IsDead.Should().BeTrue();
            busy.Sandbox.IsDead.Should().BeFalse("its call finishes first");

            (await ServeAsync(busy, "run_4")).Should().Be("drain");
            unrelated.Sandbox.IsDead.Should().BeFalse();
            _pool.Counts.Total.Should().Be(1);
        }

        [Fact]
        public async Task An_unused_sandbox_goes_back_as_it_was()
        {
            var handle = await AcquireAsync();
            await _pool.ReturnUnusedAsync(handle);

            _pool.Counts.Should().Be((1, 0, 1));
            (await AcquireAsync()).Sandbox.Should().BeSameAs(handle.Sandbox);
        }

        [Fact]
        public async Task Shutdown_destroys_everything_and_frees_all_memory()
        {
            var a = await AcquireAsync();
            await ServeAsync(a, "run_1");
            await AcquireAsync(Key with { TenantId = "tenant_z" });

            await _pool.DisposeAsync();

            _factory.Created.Should().OnlyContain(c => c.Disposed);
            _budget.CommittedMemoryBytes.Should().Be(0);
            _pool.Counts.Total.Should().Be(0);
            (await _pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.NoCapacity);
        }

        [Fact]
        public async Task The_pool_tracks_the_names_the_reaper_must_leave_alone()
        {
            var handle = await AcquireAsync();

            _pool.IsTracked(handle.Sandbox.Name).Should().BeTrue();
            _pool.IsTracked("blocks-fnwarm-someone-else").Should().BeFalse();
        }

        [Fact]
        public async Task Concurrent_callers_never_share_a_sandbox()
        {
            await _pool.PrewarmAsync(Key, RunLimits.Default, 3, default);

            var handles = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AcquireAsync()));

            handles.Select(h => h.Sandbox).Distinct().Should().HaveCount(8);
            handles.Count(h => h.Reused).Should().Be(3);
        }
    }
}
