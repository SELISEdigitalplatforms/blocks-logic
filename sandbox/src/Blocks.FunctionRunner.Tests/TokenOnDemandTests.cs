using System.Text.Json;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The caller's token on demand: a warm sandbox asks for it with a `need` line only when the
    /// function's code calls `ctx.blocks.getAccessToken()`; the runner fetches it once per call and
    /// answers with a `give` line on stdin — never for another call, never more than the cap.
    /// </summary>
    public class TokenOnDemandTests
    {
        private static string Need(string call, long id, string what = "accessToken") =>
            JsonSerializer.Serialize(new { t = "need", call, what, id });

        private static async Task<(ReusableSandbox Sandbox, ScriptedContainer Container)> ReadyAsync(Action<ScriptedContainer> script)
        {
            var container = new ScriptedContainer();
            script(container);
            var sandbox = new ReusableSandbox(container, new Options.RunnerOptions(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            (await sandbox.StartAsync(CancellationToken.None)).Status.Should().Be(WarmStartStatus.Ready);
            return (sandbox, container);
        }

        [Fact]
        public async Task A_runtime_that_announces_need_at_ready_can_ask_and_an_older_one_cannot()
        {
            var (fresh, _) = await ReadyAsync(c => { c.OnStart.Clear(); c.OnStart.Add("""{"t":"ready","at":1,"can":["need"]}"""); });
            await using (fresh) fresh.CanAsk.Should().BeTrue();

            var (older, _) = await ReadyAsync(_ => { });
            await using (older) older.CanAsk.Should().BeFalse("an image built on an older runtime is sent the token up front");
        }

        [Fact]
        public async Task A_need_is_answered_once_per_call_with_the_fetched_token()
        {
            var (sandbox, container) = await ReadyAsync(c =>
            {
                c.OnCall = id => [Lines.Started(id), Need(id, 1), Need(id, 2)];
                c.OnGive = give => give.GetProperty("id").GetInt64() == 2
                    ? [Lines.Result(give.GetProperty("call").GetString()!, "1"), Lines.Idle(give.GetProperty("call").GetString()!, clean: true)]
                    : [];
            });
            await using var _ = sandbox;
            var fetches = 0;

            var call = await sandbox.RunCallAsync("run_tok", Lines.Envelope("run_tok"), RunLimits.Default, null, null,
                CancellationToken.None, _ => { Interlocked.Increment(ref fetches); return Task.FromResult<string?>("tok-for-run_tok"); });

            call.Discard.Should().BeNull();
            fetches.Should().Be(1, "fetched once, however often the call asks");
            container.Gives.Should().HaveCount(2);
            container.Gives.Should().OnlyContain(g => g.StartsWith("{\"t\":\"give\"", StringComparison.Ordinal) && g.Contains("tok-for-run_tok"));
        }

        [Fact]
        public async Task A_need_for_another_call_is_never_answered()
        {
            var (sandbox, container) = await ReadyAsync(c =>
                c.OnCall = id => [Lines.Started(id), Need("someone_else", 1), Lines.Result(id, "1"), Lines.Idle(id, clean: true)]);
            await using var _ = sandbox;
            var fetches = 0;

            await sandbox.RunCallAsync("run_mine", Lines.Envelope("run_mine"), RunLimits.Default, null, null,
                CancellationToken.None, _ => { fetches++; return Task.FromResult<string?>("tok"); });

            fetches.Should().Be(0);
            container.Gives.Should().BeEmpty();
        }

        [Fact]
        public async Task Without_a_caller_token_the_answer_is_null_not_silence()
        {
            var (sandbox, container) = await ReadyAsync(c =>
            {
                c.OnCall = id => [Lines.Started(id), Need(id, 1)];
                c.OnGive = give => [Lines.Result(give.GetProperty("call").GetString()!, "1"), Lines.Idle(give.GetProperty("call").GetString()!, clean: true)];
            });
            await using var _ = sandbox;

            await sandbox.RunCallAsync("run_anon", Lines.Envelope("run_anon"), RunLimits.Default, null, null,
                CancellationToken.None, _ => Task.FromResult<string?>(null));

            container.Gives.Should().ContainSingle().Which.Should().Contain("\"value\":null");
        }

        [Fact]
        public async Task A_failing_fetch_is_answered_with_null()
        {
            var (sandbox, container) = await ReadyAsync(c =>
            {
                c.OnCall = id => [Lines.Started(id), Need(id, 1)];
                c.OnGive = give => [Lines.Result(give.GetProperty("call").GetString()!, "1"), Lines.Idle(give.GetProperty("call").GetString()!, clean: true)];
            });
            await using var _ = sandbox;

            var call = await sandbox.RunCallAsync("run_fail", Lines.Envelope("run_fail"), RunLimits.Default, null, null,
                CancellationToken.None, _ => throw new HttpRequestException("IAM down"));

            call.Discard.Should().BeNull();
            container.Gives.Should().ContainSingle().Which.Should().Contain("\"value\":null");
        }

        [Fact]
        public async Task A_call_flooding_needs_is_ended_as_a_protocol_breach()
        {
            var (sandbox, _) = await ReadyAsync(c =>
                c.OnCall = id => [Lines.Started(id), .. Enumerable.Range(1, ReusableSandbox.MaxAsksPerCall + 1).Select(n => Need(id, n))]);
            await using var _ = sandbox;

            var call = await sandbox.RunCallAsync("run_flood", Lines.Envelope("run_flood"), RunLimits.Default, null, null,
                CancellationToken.None, _ => Task.FromResult<string?>("tok"));

            call.Discard.Should().Be("protocol");
        }
    }
}
