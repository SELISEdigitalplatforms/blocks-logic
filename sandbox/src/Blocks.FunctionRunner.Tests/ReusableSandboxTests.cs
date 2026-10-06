using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The reuse protocol from the runner's side: which lines belong to a call, when a call is
    /// over, and every way a sandbox can stop being trustworthy — each of which must end with the
    /// container killed and a reason the run record can show.
    /// </summary>
    public class ReusableSandboxTests
    {
        private static readonly RunnerOptions Options = new() { StartupAllowanceSeconds = 30, KillGraceSeconds = 2 };

        private static async Task<(ReusableSandbox Sandbox, ScriptedContainer Container)> ReadyAsync(
            Action<ScriptedContainer>? configure = null)
        {
            var container = new ScriptedContainer();
            configure?.Invoke(container);
            var sandbox = new ReusableSandbox(container, Options, NullLogger.Instance);
            var start = await sandbox.StartAsync(CancellationToken.None);
            start.Status.Should().Be(WarmStartStatus.Ready);
            return (sandbox, container);
        }

        private static Task<WarmCallResult> CallAsync(ReusableSandbox sandbox, string id, long? startupMs = null, CancellationToken token = default)
            => sandbox.RunCallAsync(id, Lines.Envelope(id), RunLimits.Default, startupMs, null, token);

        [Fact]
        public async Task A_clean_call_returns_the_single_run_outcome_and_keeps_the_sandbox()
        {
            var (sandbox, container) = await ReadyAsync(c => c.OnCall = id =>
            [
                Lines.Started(id),
                Lines.Log(id, "hello"),
                Lines.Result(id, """{"n":1.50}"""),
                Lines.Idle(id, clean: true),
            ]);

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().BeNull();
            call.Clean.Should().BeTrue();
            call.Result.Output.Ok.Should().BeTrue();
            call.Result.Output.ResultJson.Should().Be("""{"n":1.50}""", "the raw value, digits and all");
            call.Result.Output.Logs.Should().ContainSingle().Which.Should().Contain("hello");
            call.Result.ExitCode.Should().Be(0);
            call.Result.StartupMs.Should().Be(0, "a call on a running sandbox has no startup");
            call.Result.PeakMemoryBytes.Should().Be(container.MemoryBytes, "memory comes from Docker's stats");
            call.Result.CpuUsageMs.Should().BePositive();
            container.Killed.Should().BeFalse();
            container.Written.Should().ContainSingle().Which.Should().Be(Lines.Envelope("run_1"));

            RunOutcome.Map(call.Result.OomKilled, call.Result.ExitCode, call.Result.TimedOut, false, call.Result.Output)
                .Status.Should().Be(RunStatuses.Succeeded);
        }

        [Fact]
        public async Task The_first_call_reports_the_sandbox_start_as_its_startup()
        {
            var (sandbox, _) = await ReadyAsync();

            var call = await CallAsync(sandbox, "run_1", startupMs: 2500);

            call.Result.StartupMs.Should().BeGreaterThanOrEqualTo(2500);
            call.Result.DurationMs.Should().BeGreaterThanOrEqualTo(2500);
        }

        [Fact]
        public async Task Lines_of_other_calls_and_late_lines_are_not_this_calls_output()
        {
            var (sandbox, _) = await ReadyAsync(c => c.OnCall = id =>
            [
                Lines.Log("run_old", "from an earlier call", late: true),
                Lines.Log("none", "module background", late: true),
                Lines.Started(id),
                Lines.Log(id, "mine"),
                Lines.Result(id, "1"),
                Lines.Idle(id, clean: true),
            ]);

            var call = await CallAsync(sandbox, "run_2");

            call.Result.Output.Logs.Should().ContainSingle().Which.Should().Contain("mine");
        }

        [Fact]
        public async Task Module_level_logs_written_while_loading_go_to_the_first_call()
        {
            var (sandbox, _) = await ReadyAsync(c => c.OnStart.Insert(0, Lines.Log("none", "connecting")));

            var first = await CallAsync(sandbox, "run_1", startupMs: 10);
            var second = await CallAsync(sandbox, "run_2");

            first.Result.Output.Logs.Should().Contain(l => l.Contains("connecting"));
            second.Result.Output.Logs.Should().NotContain(l => l.Contains("connecting"));
        }

        [Fact]
        public async Task A_dirty_call_is_answered_and_the_sandbox_is_killed_with_its_leftovers()
        {
            var (sandbox, container) = await ReadyAsync(c => c.OnCall = id =>
            [
                Lines.Started(id), Lines.Result(id, "1"), Lines.Idle(id, clean: false, ["Timeout", "fetch x1"]),
            ]);

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().Be("dirty:Timeout,fetch x1");
            call.Result.Output.Ok.Should().BeTrue("the caller still gets its answer");
            container.Killed.Should().BeTrue();
            sandbox.IsDead.Should().BeTrue();
        }

        [Fact]
        public async Task Late_activity_alone_makes_a_call_dirty()
        {
            var (sandbox, _) = await ReadyAsync(c => c.OnCall = id =>
                [Lines.Started(id), Lines.Result(id, "1"), Lines.Idle(id, clean: false, late: true)]);

            (await CallAsync(sandbox, "run_1")).Discard.Should().Be("dirty:late");
        }

        [Fact]
        public async Task A_timeout_the_runtime_caught_is_a_timeout()
        {
            var (sandbox, _) = await ReadyAsync(c => c.OnCall = id =>
            [
                Lines.Started(id),
                Lines.Failure(id, ErrorCodes.TimedOut, "the function exceeded its 30000 ms time limit"),
                Lines.Idle(id, clean: false, ["timeout"]),
                Lines.Fatal(ErrorCodes.TimedOut, "call exceeded its time limit"),
            ]);

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().Be("timeout");
            RunOutcome.Map(call.Result.OomKilled, call.Result.ExitCode, call.Result.TimedOut, false, call.Result.Output)
                .Status.Should().Be(RunStatuses.TimedOut);
        }

        [Fact]
        public async Task A_sandbox_that_exits_mid_call_is_a_crash()
        {
            ScriptedContainer? container = null;
            var (sandbox, c) = await ReadyAsync(x =>
            {
                container = x;
                x.Exit = (10, false);
                x.OnCall = id => { container!.End(); return [Lines.Started(id)]; };
            });

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().Be("crash");
            call.Result.ExitCode.Should().Be(10);
            sandbox.IsDead.Should().BeTrue();
        }

        [Fact]
        public async Task An_oom_kill_is_reported_as_memory()
        {
            ScriptedContainer? container = null;
            var (sandbox, _) = await ReadyAsync(x =>
            {
                container = x;
                x.Exit = (137, true);
                x.OnCall = id => { container!.End(); return [Lines.Started(id)]; };
            });

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().Be("memory");
            call.Result.OomKilled.Should().BeTrue();
        }

        [Fact]
        public async Task An_idle_for_another_call_is_a_protocol_violation()
        {
            var (sandbox, container) = await ReadyAsync(c => c.OnCall = id =>
                [Lines.Started(id), Lines.Result(id, "1"), Lines.Idle("someone_else", clean: true)]);

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().Be("protocol");
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task Output_past_the_ceiling_ends_the_sandbox()
        {
            var big = new string('x', 60 * 1024);
            var (sandbox, container) = await ReadyAsync(c => c.OnCall = id =>
                Enumerable.Range(0, 200).Select(_ => Lines.Log(id, big)));

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().Be("protocol");
            call.Result.Output.Truncated.Should().BeTrue();
            call.Result.Output.LogBytes.Should().BeLessThanOrEqualTo(Ceilings.LogBytes);
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task A_cancelled_call_kills_the_sandbox()
        {
            var (sandbox, container) = await ReadyAsync(c => c.OnCall = id => [Lines.Started(id)]);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var call = await CallAsync(sandbox, "run_1", token: cts.Token);

            call.Discard.Should().Be("cancelled");
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task Stray_stdout_is_recorded_as_malformed_and_harmless()
        {
            var (sandbox, _) = await ReadyAsync(c => c.OnCall = id =>
                ["a package wrote this", Lines.Started(id), Lines.Result(id, "1"), Lines.Idle(id, clean: true)]);

            var call = await CallAsync(sandbox, "run_1");

            call.Discard.Should().BeNull();
            call.Result.Output.Malformed.Should().ContainSingle();
        }

        // ---- start --------------------------------------------------------------

        [Fact]
        public async Task A_function_that_fails_to_load_is_the_tenants_failure()
        {
            var container = new ScriptedContainer { Exit = (10, false) };
            container.OnStart.Clear();
            container.OnStart.Add(Lines.Fatal(ErrorCodes.UserRuntimeError, "the function module failed to load: boom"));
            var sandbox = new ReusableSandbox(container, Options, NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);

            start.Status.Should().Be(WarmStartStatus.LoadFailed);
            container.Killed.Should().BeTrue();
            var (status, code, message) = RunOutcome.Map(start.OomKilled, start.ExitCode, start.TimedOut, false, start.Output);
            status.Should().Be(RunStatuses.Failed);
            code.Should().Be(ErrorCodes.UserRuntimeError);
            message.Should().Contain("failed to load");
        }

        [Fact]
        public async Task An_image_built_on_the_v1_runtime_has_no_reuse_support()
        {
            var container = new ScriptedContainer { Exit = (20, false) };
            container.OnStart.Clear();
            container.OnStart.Add(Lines.V1NoEnvelope());
            var sandbox = new ReusableSandbox(container, Options, NullLogger.Instance);

            (await sandbox.StartAsync(CancellationToken.None)).Status.Should().Be(WarmStartStatus.NoReuseSupport);
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task A_bootstrap_that_cannot_import_reuse_mjs_has_no_reuse_support()
        {
            var container = new ScriptedContainer { Exit = (20, false) };
            container.OnStart.Clear();
            container.OnStart.Add(Lines.Failure("x", ErrorCodes.RuntimeStartFailed,
                "runtime 1 failed: Cannot find module '/runtime/reuse.mjs' imported from /runtime/bootstrap.mjs"));
            var sandbox = new ReusableSandbox(container, Options, NullLogger.Instance);

            (await sandbox.StartAsync(CancellationToken.None)).Status.Should().Be(WarmStartStatus.NoReuseSupport);
        }

        [Fact]
        public async Task A_start_past_the_allowance_is_killed_as_timed_out()
        {
            var container = new ScriptedContainer();
            container.OnStart.Clear();
            var sandbox = new ReusableSandbox(container, new RunnerOptions { StartupAllowanceSeconds = 1 }, NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);

            start.Status.Should().Be(WarmStartStatus.LoadFailed);
            start.TimedOut.Should().BeTrue();
            container.Killed.Should().BeTrue();
        }

        [Fact]
        public async Task A_container_that_cannot_be_created_is_a_host_failure()
        {
            var container = new ScriptedContainer { StartFailure = "the sandbox could not be created: no such image" };
            var sandbox = new ReusableSandbox(container, Options, NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);

            start.Status.Should().Be(WarmStartStatus.HostFailure);
            start.HostFailure.Should().Contain("no such image");
        }

        [Theory]
        [InlineData(new string[0], false, "dirty:unknown")]
        [InlineData(new[] { "Timeout" }, false, "dirty:Timeout")]
        [InlineData(new[] { "Interval", "fetch x2" }, true, "dirty:Interval,fetch x2,late")]
        [InlineData(new[] { "timeout" }, false, "timeout")]
        [InlineData(new[] { "waitUntil" }, false, "timeout")]
        [InlineData(new[] { "crash" }, false, "crash")]
        public void Dirty_reasons_name_what_was_left(string[] leftovers, bool late, string expected)
            => ReusableSandbox.DirtyReason(leftovers, late).Should().Be(expected);
    }
}
