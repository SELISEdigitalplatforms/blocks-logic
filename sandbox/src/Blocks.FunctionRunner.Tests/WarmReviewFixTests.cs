using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The failure paths an independent review of the reuse code found open (2026-10-06): a
    /// wedged sandbox stalling the run loop, a stray cancellation ending it, starts that leaked,
    /// eviction for the wrong reason, start charges for nothing, double hand-backs, and memory
    /// read the wrong way. One test (or a few) per finding.
    /// </summary>
    public sealed class WarmReviewFixTests
    {
        private static readonly WarmKey Key = new("tenant_a", "fn_1", "v1", "img@sha256:aa");

        private static (WarmPool Pool, ScriptedFactory Factory, HostBudget Budget) Pool(
            Action<RunnerOptions>? configure = null, CalmHost? host = null, ManualTime? time = null)
        {
            var options = new RunnerOptions { MaxActiveSandboxes = 50, StartCostCpuMs = 0 };
            configure?.Invoke(options);
            var wrapped = Microsoft.Extensions.Options.Options.Create(options);
            var budget = new HostBudget(wrapped, host ?? new CalmHost(), new SandboxFootprint(),
                NullLogger<HostBudget>.Instance, time ?? new ManualTime());
            var factory = new ScriptedFactory(options);
            return (new WarmPool(factory, budget, wrapped, NullLogger<WarmPool>.Instance, time), factory, budget);
        }

        private static async Task<(ReusableSandbox Sandbox, ScriptedContainer Container)> ReadyAsync(
            RunnerOptions options, Action<ScriptedContainer>? configure = null)
        {
            var container = new ScriptedContainer();
            configure?.Invoke(container);
            var sandbox = new ReusableSandbox(container, options, NullLogger.Instance);
            (await sandbox.StartAsync(CancellationToken.None)).Status.Should().Be(WarmStartStatus.Ready);
            return (sandbox, container);
        }

        // ---- 1: the envelope write has a deadline ------------------------------------------

        [Fact]
        public async Task A_write_that_never_completes_is_cut_off_and_the_sandbox_killed()
        {
            var (sandbox, container) = await ReadyAsync(new RunnerOptions { WarmWriteTimeoutMs = 200 }, c => c.BlockWrites = true);

            var started = DateTime.UtcNow;
            var call = await sandbox.RunCallAsync("run_1", Lines.Envelope("run_1"), RunLimits.Default, null, null, default);

            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
            call.Discard.Should().Be("timeout");
            call.Started.Should().BeFalse();
            call.Result.HostFailure.Should().NotBeNull();
            container.Killed.Should().BeTrue();
        }

        // ---- 2: a cancellation that is not a shutdown never ends the run loop ---------------

        [Fact]
        public async Task A_cancellation_during_the_write_is_a_result_not_an_exception()
        {
            var (sandbox, container) = await ReadyAsync(new RunnerOptions { WarmWriteTimeoutMs = 60_000 }, c => c.BlockWrites = true);
            using var lease = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var call = await sandbox.RunCallAsync("run_1", Lines.Envelope("run_1"), RunLimits.Default, null, null, lease.Token);

            call.Discard.Should().Be("cancelled");
            call.Result.HostFailure.Should().BeNull("a cancelled run is reported as cancelled, not as a host fault");
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task A_cancellation_that_is_not_a_shutdown_leaves_the_run_pending_and_the_loop_running()
        {
            var disposition = await RunConsumerService.GuardAsync(
                () => throw new TaskCanceledException("HttpClient.Timeout of 100 seconds elapsed"),
                "run_1", NullLogger.Instance, CancellationToken.None);

            disposition.Should().Be(RunProcessor.Disposition.Deferred);
        }

        [Fact]
        public async Task A_shutdown_still_stops_the_loop()
        {
            using var stopping = new CancellationTokenSource();
            await stopping.CancelAsync();

            var act = () => RunConsumerService.GuardAsync(
                () => throw new OperationCanceledException(stopping.Token), "run_1", NullLogger.Instance, stopping.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // ---- 3: a start that throws leaks nothing ---------------------------------------------

        [Fact]
        public async Task A_start_that_throws_leaves_no_entry_memory_or_container()
        {
            var (pool, factory, budget) = Pool();
            await using var _ = pool;
            factory.Configure = c => c.StartThrows = new TaskCanceledException("HttpClient.Timeout elapsed");

            var result = await pool.AcquireAsync(Key, RunLimits.Default, CancellationToken.None);

            result.Status.Should().Be(WarmAcquireStatus.StartFailed);
            result.Start!.Status.Should().Be(WarmStartStatus.HostFailure);
            pool.Counts.Total.Should().Be(0);
            budget.CommittedMemoryBytes.Should().Be(0);
            budget.Warm.Should().Be(0);
            factory.Created[0].Disposed.Should().BeTrue();
        }

        [Fact]
        public async Task An_unreadable_exit_state_after_a_failed_load_still_kills_the_container()
        {
            var container = new ScriptedContainer { ExitThrows = true };
            container.OnStart.Clear();
            container.OnStart.Add(Lines.Fatal(ErrorCodes.UserRuntimeError, "the function module failed to load: x"));
            var sandbox = new ReusableSandbox(container, new RunnerOptions(), NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);

            start.Status.Should().Be(WarmStartStatus.LoadFailed);
            container.Killed.Should().BeTrue();
        }

        // ---- 4: evict only for memory -------------------------------------------------------

        [Fact]
        public void A_warm_refusal_says_why()
        {
            var time = new ManualTime();
            var tight = new HostBudget(
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions { StartCostCpuMs = 0, MaxActiveSandboxes = 10 }),
                new CalmHost { TotalMemoryBytes = 2048L * 1024 * 1024 }, new SandboxFootprint(), NullLogger<HostBudget>.Instance, time);
            tight.TryReserveWarm(Ceilings.MemoryBytes, out var memory).Should().BeNull();
            memory.Should().Be(AdmissionRefusal.Memory);

            var slow = new HostBudget(
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions { StartCostCpuMs = 800, StartsPerSecondPerCore = 1, MaxActiveSandboxes = 10 }),
                new CalmHost { Cores = 1 }, new SandboxFootprint(), NullLogger<HostBudget>.Instance, time);
            using var first = slow.TryReserveWarm(Ceilings.MemoryBytes, out _);
            slow.TryReserveWarm(Ceilings.MemoryBytes, out var rate).Should().BeNull();
            rate.Should().Be(AdmissionRefusal.StartRate);
        }

        [Fact]
        public async Task A_start_refused_for_the_start_rate_evicts_nothing()
        {
            var time = new ManualTime();
            var (pool, _, _) = Pool(o => { o.StartCostCpuMs = 800; o.StartsPerSecondPerCore = 1; }, new CalmHost { Cores = 1 }, time);
            await using var __ = pool;

            (await pool.PrewarmAsync(Key with { FunctionId = "other" }, RunLimits.Default, 1, default)).Should().Be(1);

            (await pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().Be(WarmAcquireStatus.NoCapacity);
            pool.Counts.Total.Should().Be(1, "evicting cannot refill the start bucket, so the idle sandbox stays");
        }

        // ---- 5: a single run can take memory back from idle warm sandboxes -------------------

        [Fact]
        public async Task An_idle_warm_sandbox_can_be_evicted_to_make_room()
        {
            var host = new CalmHost { TotalMemoryBytes = (2048L + 128) * 1024 * 1024 };
            var (pool, _, budget) = Pool(host: host);
            await using var _ = pool;
            await pool.PrewarmAsync(Key, RunLimits.Default, 1, default);

            budget.TryReserve(Ceilings.MemoryBytes, out var refusal).Should().BeNull();
            refusal.Should().Be(AdmissionRefusal.Memory);

            (await pool.EvictIdleAsync()).Should().BeTrue();
            using var single = budget.TryReserve(Ceilings.MemoryBytes);
            single.Should().NotBeNull();
            (await pool.EvictIdleAsync()).Should().BeFalse("nothing idle is left");
        }

        // ---- 6: a start charge without a start is refunded ----------------------------------

        [Fact]
        public void A_reservation_that_never_started_a_container_gives_its_start_back()
        {
            var budget = new HostBudget(
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions { StartCostCpuMs = 800, StartsPerSecondPerCore = 1, MaxActiveSandboxes = 10 }),
                new CalmHost { Cores = 1 }, new SandboxFootprint(), NullLogger<HostBudget>.Instance, new ManualTime());

            // Deferred after admission (tenant slot, function slot, lease): nothing started.
            budget.TryReserve(Ceilings.MemoryBytes)!.Dispose();
            using (var started = budget.TryReserve(Ceilings.MemoryBytes))
            {
                started.Should().NotBeNull("the deferred run's start came back");
                started!.ContainerStarted();
            }

            budget.TryReserve(Ceilings.MemoryBytes).Should().BeNull("a start that happened stays spent");
        }

        // ---- 7: a reused sandbox that dies before `started` is not the call's failure --------

        [Fact]
        public async Task A_sandbox_that_exits_before_started_reports_the_call_as_not_started()
        {
            ScriptedContainer? self = null;
            var (sandbox, _) = await ReadyAsync(new RunnerOptions(), c => { self = c; c.OnCall = _ => { self!.End(); return []; }; });

            var call = await sandbox.RunCallAsync("run_1", Lines.Envelope("run_1"), RunLimits.Default, null, null, default);

            call.Started.Should().BeFalse();
            call.Discard.Should().Be("crash");
        }

        [Fact]
        public async Task A_sandbox_that_will_not_resume_reports_the_call_as_not_started()
        {
            var (pool, factory, _) = Pool();
            await using var _ = pool;
            await pool.PrewarmAsync(Key, RunLimits.Default, 1, default);
            factory.Created[0].FailUnpause = true;
            var handle = (await pool.AcquireAsync(Key, RunLimits.Default, default)).Handle!;

            var call = await handle.RunCallAsync("run_1", Lines.Envelope("run_1"), RunLimits.Default, null, default);

            call.Started.Should().BeFalse();
            call.Discard.Should().Be("protocol");
            factory.Created[0].Written.Should().BeEmpty("nothing was handed to a sandbox that is not running");
        }

        [Fact]
        public async Task A_fresh_sandbox_can_be_insisted_on()
        {
            var (pool, factory, _) = Pool();
            await using var _ = pool;
            await pool.PrewarmAsync(Key, RunLimits.Default, 1, default);

            var fresh = (await pool.AcquireAsync(Key, RunLimits.Default, default, allowIdle: false)).Handle!;

            fresh.Reused.Should().BeFalse();
            factory.Created.Should().HaveCount(2);
        }

        // ---- 8: drained or shut down while starting ----------------------------------------

        [Fact]
        public async Task A_prewarm_drained_while_starting_is_not_kept()
        {
            var (pool, factory, budget) = Pool();
            await using var _ = pool;
            factory.Configure = c => c.OnStarting = () => pool.DrainAsync(Key.TenantId, Key.FunctionId, Key.VersionId);

            (await pool.PrewarmAsync(Key, RunLimits.Default, 1, default)).Should().Be(0);

            pool.Counts.Total.Should().Be(0);
            factory.Created[0].Disposed.Should().BeTrue();
            budget.CommittedMemoryBytes.Should().Be(0);
        }

        [Fact]
        public async Task A_start_finishing_after_shutdown_is_destroyed()
        {
            var (pool, factory, budget) = Pool();
            factory.Configure = c => c.OnStarting = async () => await pool.DisposeAsync();

            (await pool.AcquireAsync(Key, RunLimits.Default, default)).Status.Should().NotBe(WarmAcquireStatus.Acquired);

            factory.Created[0].Disposed.Should().BeTrue();
            pool.Counts.Total.Should().Be(0);
            budget.CommittedMemoryBytes.Should().Be(0);
        }

        // ---- 9: concurrent starts respect the version cap -----------------------------------

        [Fact]
        public async Task Concurrent_starts_never_overshoot_the_version_cap()
        {
            var (pool, factory, _) = Pool(o => o.WarmMaxPerVersion = 2);
            await using var _ = pool;
            factory.Configure = c => c.OnStarting = () => Task.Delay(50);

            var results = await Task.WhenAll(Enumerable.Range(0, 6)
                .Select(_ => Task.Run(() => pool.AcquireAsync(Key, RunLimits.Default, default))));

            results.Count(r => r.Status == WarmAcquireStatus.Acquired).Should().Be(2);
            factory.Created.Should().HaveCount(2);
        }

        // ---- 10: a handle is handed back once --------------------------------------------------

        [Fact]
        public async Task A_handle_handed_back_twice_is_handed_back_once()
        {
            var (pool, _, _) = Pool();
            await using var _ = pool;
            var handle = (await pool.AcquireAsync(Key, RunLimits.Default, default)).Handle!;

            await pool.ReturnUnusedAsync(handle);
            var next = (await pool.AcquireAsync(Key, RunLimits.Default, default)).Handle!;
            await pool.DiscardAsync(handle, "crash");

            next.Sandbox.Should().BeSameAs(handle.Sandbox);
            next.Sandbox.IsDead.Should().BeFalse("the second hand-back of the old handle must not touch the next caller's sandbox");
        }

        // ---- 12: paused until the call is handed over -----------------------------------------

        [Fact]
        public async Task A_sandbox_from_the_pool_is_resumed_only_when_its_call_is_handed_over()
        {
            var (pool, factory, _) = Pool();
            await using var _ = pool;
            await pool.PrewarmAsync(Key, RunLimits.Default, 1, default);

            var handle = (await pool.AcquireAsync(Key, RunLimits.Default, default)).Handle!;
            factory.Created[0].Paused.Should().BeTrue();
            factory.Created[0].Unpauses.Should().Be(0);

            await pool.ReturnUnusedAsync(handle);
            factory.Created[0].Unpauses.Should().Be(0, "a run that was not runnable never woke the sandbox");

            handle = (await pool.AcquireAsync(Key, RunLimits.Default, default)).Handle!;
            await handle.RunCallAsync("run_1", Lines.Envelope("run_1"), RunLimits.Default, null, default);
            factory.Created[0].Unpauses.Should().Be(1);
        }

        // ---- 13: working set, not raw usage --------------------------------------------------------

        [Fact]
        public void Memory_is_the_working_set_not_raw_usage()
        {
            DockerReusableContainer.WorkingSetBytes(new MemoryStats
            {
                Usage = 100,
                Stats = new Dictionary<string, ulong> { ["inactive_file"] = 30 },
            }).Should().Be(70);
            DockerReusableContainer.WorkingSetBytes(new MemoryStats
            {
                Usage = 100,
                Stats = new Dictionary<string, ulong> { ["total_inactive_file"] = 40 },
            }).Should().Be(60, "cgroup v1 names it total_inactive_file");
            DockerReusableContainer.WorkingSetBytes(new MemoryStats { Usage = 100 }).Should().Be(100);
            DockerReusableContainer.WorkingSetBytes(new MemoryStats { Usage = 0 }).Should().BeNull();
        }

        // ---- 15: the default base image is one every registry already has ---------------------

        [Fact]
        public void The_default_base_image_is_the_v1_runtime()
        {
            // Hosts get v2 through deploy.sh, which builds, pushes and pins it. A host that does not
            // pin RUNNER__BaseImage must not start building FROM an image its registry may lack.
            new RunnerOptions().BaseImage.Should().EndWith("functions-node:24-v1");
        }

        // ---- 16: a failed pre-warm is still acknowledged --------------------------------------

        [Fact]
        public async Task A_prewarm_entry_that_fails_is_still_acknowledged()
        {
            var acked = 0;

            await WarmConsumerService.HandleThenAcknowledgeAsync(
                () => throw new InvalidOperationException("the registry is down"),
                () => { acked++; return Task.CompletedTask; },
                NullLogger.Instance);

            acked.Should().Be(1);
        }
    }
}
