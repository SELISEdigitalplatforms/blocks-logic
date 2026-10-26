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
        private readonly HostRoom _budget;
        private readonly ScriptedFactory _factory;
        private readonly WarmPool _pool;

        public WarmPoolTests() : this(null) { }

        private WarmPoolTests(Action<RunnerOptions>? configure)
        {
            _options = new RunnerOptions
            {
                WarmMaxCalls = 1000,
                WarmMaxAgeSeconds = 3600,
                WarmIdleSeconds = 600,
                RunnerId = "runner-test",
            };
            configure?.Invoke(_options);
            var options = Microsoft.Extensions.Options.Options.Create(_options);
            _budget = new HostRoom(options, new CalmHost(), NullLogger<HostRoom>.Instance, _time);
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

        // ---- FN-6: a version that fails to load is held (2026-10-07) ------------------------------

        private void FailToLoad() => _factory.Configure = c =>
        {
            c.OnStart.Clear();
            c.OnStart.Add(Lines.Fatal(ErrorCodes.UserRuntimeError, "the function module failed to load: SyntaxError"));
            c.Exit = (10, false);
        };

        [Fact]
        public async Task A_version_that_fails_to_load_is_answered_from_its_failure_until_the_hold_ends()
        {
            FailToLoad();

            var first = await _pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None);
            first.Status.Should().Be(WarmAcquireStatus.StartFailed);
            _factory.Created.Should().ContainSingle();

            var second = await _pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None);
            second.Status.Should().Be(WarmAcquireStatus.StartFailed);
            second.Start!.Output.ErrorMessage.Should().Contain("SyntaxError", "the same answer as the real start");
            second.Start.StartupMs.Should().Be(0, "no sandbox was made for it");
            _factory.Created.Should().ContainSingle("no new start");

            // Another version — the fix, deployed — is not held.
            _factory.Configure = null;
            (await _pool.AcquireAsync(Key with { VersionId = "v2" }, RunLimits.Default, CancellationToken.None))
                .Status.Should().Be(WarmAcquireStatus.Acquired);

            _time.Advance(TimeSpan.FromSeconds(_options.FailedLoadHoldSeconds + 1));
            (await _pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None))
                .Status.Should().Be(WarmAcquireStatus.Acquired, "the hold is over: started again");
        }

        [Fact]
        public async Task With_the_hold_off_every_call_starts_again()
        {
            await using var off = With(o => o.FailedLoadHoldSeconds = 0);
            off.FailToLoad();

            await off._pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None);
            await off._pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None);

            off._factory.Created.Should().HaveCount(2);
        }

        [Theory]
        [InlineData(false, false, ErrorCodes.UserRuntimeError, true)]
        [InlineData(true, false, ErrorCodes.TimedOut, false)]          // slow load: may pass
        [InlineData(false, true, ErrorCodes.UserRuntimeError, false)]  // OOM at load
        [InlineData(false, false, null, false)]                        // crashed without a verdict
        public void Only_the_functions_own_load_error_is_held(bool timedOut, bool oom, string? code, bool held)
        {
            var output = new Protocol.SandboxOutput();
            if (code is not null)
            {
                new Protocol.SandboxOutputParser().Feed(output,
                    System.Text.Json.JsonSerializer.Serialize(new { t = "result", ok = false, code, message = "x" }));
            }
            var start = new WarmStartResult
            {
                Status = WarmStartStatus.LoadFailed, Output = output, TimedOut = timedOut, OomKilled = oom,
            };

            WarmPool.IsOwnLoadFailure(start).Should().Be(held);
            WarmPool.IsOwnLoadFailure(start with { Status = WarmStartStatus.HostFailure }).Should().BeFalse();
        }

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
        public async Task A_dirty_call_destroys_the_sandbox()
        {
            _factory.Configure = c => c.OnCall = id =>
                [Lines.Started(id), Lines.Result(id, "1"), Lines.Idle(id, clean: false, ["Timeout"])];
            var handle = await AcquireAsync();

            (await ServeAsync(handle, "run_1")).Should().Be("dirty:Timeout");

            _factory.Created[0].Disposed.Should().BeTrue();
            _pool.Counts.Should().Be((0, 0, 0));
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
            _budget.Read().ColdStarts.Should().Be(0);
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
            WarmPool? live = null;
            // Room for one sandbox above the 2 GB reserve, read live: each sandbox alive takes its RAM.
            var host = new LiveMemoryHost(() => ((2048L + 128) * 1024 * 1024) - ((live?.Counts.Total ?? 0) * Ceilings.MemoryBytes));
            var options = Microsoft.Extensions.Options.Options.Create(new RunnerOptions());
            var budget = new HostRoom(options, host, NullLogger<HostRoom>.Instance, _time);
            var factory = new ScriptedFactory(options.Value);
            await using var pool = new WarmPool(factory, budget, options, NullLogger<WarmPool>.Instance, _time);
            live = pool;

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
        public async Task A_warm_start_needs_a_cold_start_place_which_frees_itself_after_the_boost_window()
        {
            // Three places held by starts that are never given back (the leak the old counters had).
            var held = Enumerable.Range(0, HostRoom.MaxColdStarts)
                .Select(i => _budget.TryStartCold(Ceilings.MemoryBytes, out _)).ToList();
            held.Should().OnlyContain(h => h != null);

            (await _pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.NoCapacity);

            // Past the start boost's window (5 s) they no longer count, released or not: no restart needed.
            _time.Advance(TimeSpan.FromSeconds(6));
            (await _pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.Acquired);
        }

        [Fact]
        public async Task A_function_keeps_at_most_three_sandboxes_by_default_across_its_versions()
        {
            (await _pool.WarmUpAsync(Key, 10)).Should().Be(3, "the default concurrency is 3 (RUNNER-FLOW step 5.6)");

            (await _pool.AcquireAsync(Key with { VersionId = "v2" }, RunLimits.Default, default, allowIdle: false)).Status
                .Should().Be(WarmAcquireStatus.NoCapacity, "the cap is per function, not per version");
        }

        [Fact]
        public async Task A_function_set_to_more_may_keep_that_many()
        {
            var five = RunLimits.Clamp(null, null, null, null, null, 5);
            var handles = new List<WarmHandle>();
            for (var i = 0; i < 6; i++)
            {
                var acquired = await _pool.AcquireAsync(Key, five, default, allowIdle: false);
                if (acquired.Handle is not null) handles.Add(acquired.Handle);
                _time.Advance(TimeSpan.FromSeconds(6));   // each start's place frees itself
            }

            handles.Should().HaveCount(5);
        }

        [Fact]
        public async Task Draining_and_dead_sandboxes_do_not_hold_a_place_under_the_cap()
        {
            await _pool.WarmUpAsync(Key, 3);
            await _pool.DrainAsync(Key.TenantId, Key.FunctionId, Key.VersionId);
            await _pool.WarmUpAsync(Key with { VersionId = "v2" }, 3);
            // One of the new version's sandboxes is found dead by the clean-up.
            (await _pool.RemoveDeadAsync(_factory.Created[^1].Name)).Should().BeTrue();

            var fresh = await _pool.AcquireAsync(Key with { VersionId = "v2" }, RunLimits.Default, default, allowIdle: false);

            fresh.Status.Should().Be(WarmAcquireStatus.Acquired, "only the live ones count");
        }

        [Fact]
        public async Task The_clean_up_keeps_one_idle_sandbox_per_function_for_600_s_and_removes_extras_after_60_s()
        {
            await _pool.WarmUpAsync(Key, 3);
            _time.Advance(TimeSpan.FromSeconds(30));
            var newest = await AcquireAsync();
            await ServeAsync(newest, "run_1");

            _time.Advance(TimeSpan.FromSeconds(61));
            (await _pool.SweepAsync()).Should().Be(2, "the two extras were idle more than 60 s");
            _pool.Counts.Total.Should().Be(1);
            newest.Sandbox.IsDead.Should().BeFalse("the most recently used one is the one kept");

            _time.Advance(TimeSpan.FromSeconds(540));
            (await _pool.SweepAsync()).Should().Be(1, "the kept one goes after 600 s idle");
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
        public async Task Shutdown_destroys_everything()
        {
            var a = await AcquireAsync();
            await ServeAsync(a, "run_1");
            await AcquireAsync(Key with { TenantId = "tenant_z" });

            await _pool.DisposeAsync();

            _factory.Created.Should().OnlyContain(c => c.Disposed);
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
            await _pool.WarmUpAsync(Key, 3);

            var handles = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => AcquireAsync()));

            handles.Select(h => h.Sandbox).Distinct().Should().HaveCount(3);
            handles.Should().OnlyContain(h => h.Reused);
            (await _pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.NoCapacity,
                "a fourth call at the same time waits in the queue for one of the three");
        }
    }
}
