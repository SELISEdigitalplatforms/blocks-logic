using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// The container behind a <see cref="ReusableSandbox"/>, reduced to what the reuse protocol
    /// needs from it. Exists so the protocol — the part with the interesting failure modes — can be
    /// driven line by line in a test, without a Docker Engine, while the Docker half stays a thin
    /// pass-through (<see cref="DockerReusableContainer"/>).
    /// </summary>
    public interface IReusableContainer : IAsyncDisposable
    {
        /// <summary>The container's name, which the reaper and the pool track it by.</summary>
        string Name { get; }

        /// <summary>
        /// Creates the container, verifies its security profile, attaches to it and starts it.
        /// </summary>
        /// <returns>null when it is running, otherwise why it could not be started.</returns>
        Task<string?> StartAsync(CancellationToken token);

        /// <summary>
        /// The next line the sandbox wrote (stdout and stderr interleaved, as for a run), or null
        /// once the stream has ended. A line longer than the reader's ceiling arrives as
        /// <see cref="ReusableSandbox.OverflowLine"/> instead of its content.
        /// </summary>
        ValueTask<string?> ReadLineAsync(CancellationToken token);

        /// <summary>Writes <paramref name="line"/> and a newline to the sandbox's stdin.</summary>
        Task WriteLineAsync(string line, CancellationToken token);

        Task PauseAsync(CancellationToken token);

        Task UnpauseAsync(CancellationToken token);

        /// <summary>
        /// The cgroup's current memory use and its cumulative CPU time, from Docker's stats.
        /// Either is null when Docker did not say. Never the runtime's own <c>rssBytes</c>.
        /// </summary>
        Task<(long? MemoryBytes, long? CpuTotalMs)> StatsAsync(CancellationToken token);

        /// <summary>
        /// The container's cumulative CPU time as the host kernel charges it (cgroup v2
        /// <c>cpu.stat</c> <c>usage_usec</c>), read on the spot, or null when it cannot be read.
        /// This is the counter the CPU limit is enforced against. The runtime's own
        /// <c>process.cpuUsage()</c> is not: under gVisor it counts time the host throttled the
        /// sandbox as CPU used — measured 2026-10-06 at 480 ms "used" for 110 ms really charged in a
        /// 1 s busy loop at 0.1 CPU, which is how a warm call showed "192 / 100 m".
        /// </summary>
        long? HostCpuMicroseconds() => null;

        /// <summary>SIGKILL. Quiet when the container is already gone.</summary>
        Task KillAsync();

        /// <summary>
        /// How the container ended, waiting briefly for it to stop when it is still going down.
        /// Null when it could not be read (or it is, against expectation, still running).
        /// </summary>
        Task<(int ExitCode, bool OomKilled)?> ExitStateAsync();
    }

    /// <summary>How starting a reusable sandbox went.</summary>
    public enum WarmStartStatus
    {
        /// <summary>The function's module loaded and the sandbox said <c>ready</c>.</summary>
        Ready,

        /// <summary>
        /// The image cannot do reuse at all: it was built FROM a runtime image without
        /// <c>reuse.mjs</c> (24-v1). Its bootstrap ignores the mode, finds no execution.json and
        /// fails as a bootstrap fault (<c>RUNTIME_START_FAILED</c>). Not the tenant's fault and not
        /// a failed run: the caller serves the run the single-run way instead.
        /// </summary>
        NoReuseSupport,

        /// <summary>
        /// The function itself failed to load (<c>fatal</c> before <c>ready</c>), or the start ran
        /// past its allowance. The run fails exactly as a single run of the same version would.
        /// </summary>
        LoadFailed,

        /// <summary>The container could not be created or started; the host's fault.</summary>
        HostFailure,
    }

    /// <summary>The outcome of <see cref="ReusableSandbox.StartAsync"/>.</summary>
    public sealed record WarmStartResult
    {
        public required WarmStartStatus Status { get; init; }

        /// <summary>Container start to <c>ready</c> (or to the failure), runner clock.</summary>
        public long StartupMs { get; init; }

        /// <summary>
        /// For <see cref="WarmStartStatus.LoadFailed"/>: what the sandbox said, shaped like a
        /// run's output so it maps through <see cref="RunOutcome.Map"/> the same way.
        /// </summary>
        public SandboxOutput Output { get; init; } = new();

        public int ExitCode { get; init; } = -1;
        public bool OomKilled { get; init; }
        public bool TimedOut { get; init; }

        /// <summary>For <see cref="WarmStartStatus.HostFailure"/>: why.</summary>
        public string? HostFailure { get; init; }
    }

    /// <summary>What one call on a reusable sandbox produced, beyond the run's own result.</summary>
    public sealed record WarmCallResult
    {
        /// <summary>The run's outcome, in exactly the shape the single-run path produces.</summary>
        public required SandboxResult Result { get; init; }

        /// <summary>The runtime's verdict on the call (its <c>idle</c> line); false when there was none.</summary>
        public bool Clean { get; init; }

        public IReadOnlyList<string> Leftovers { get; init; } = [];

        /// <summary>Code of an earlier call (or module background) ran during this one.</summary>
        public bool Late { get; init; }

        /// <summary>
        /// Why this sandbox must not serve another call — <c>dirty:…</c>, <c>timeout</c>,
        /// <c>crash</c>, <c>protocol</c>, <c>cancelled</c> — or null when the call itself gave no
        /// reason (the pool may still find one: calls, age, memory).
        /// </summary>
        public string? Discard { get; init; }

        /// <summary>Claim to envelope written, from the caller's stopwatch; null when never written.</summary>
        public long? HandoverMs { get; init; }

        /// <summary>
        /// The cgroup's working set right after the call (usage minus inactive file cache, as the
        /// docker CLI computes it), from Docker's stats. A snapshot, not a peak.
        /// </summary>
        public long? MemoryBytes { get; init; }

        /// <summary>
        /// The call's own <c>started</c> line arrived: from here on any failure is the call's own.
        /// A sandbox that dies before it failed nobody's code — the caller may serve the run again
        /// on a fresh sandbox.
        /// </summary>
        public bool Started { get; init; }
    }

    /// <summary>
    /// One long-lived sandbox in reuse mode (sandbox/REUSE.md): the runtime imports the function
    /// once and then serves one call per stdin line, strictly one after another.
    /// <para>
    /// Everything the single-run path promises still holds per call, which is what this type is
    /// for: the same security profile (<see cref="SandboxProfile.CreateReusable"/>), the same hard
    /// deadline enforced by a kill from outside, the same out-of-process output ceilings, the same
    /// result shape. What is new is that a call ends on a protocol line (<c>idle</c>) rather than on
    /// an exit — so every way that line can fail to arrive, or arrive wrong, ends with the
    /// container killed rather than trusted.
    /// </para>
    /// <para>
    /// Not thread-safe by design: the pool hands a sandbox to one caller at a time.
    /// </para>
    /// </summary>
    public sealed class ReusableSandbox : IAsyncDisposable
    {
        /// <summary>What the line reader yields in place of a line over its ceiling.</summary>
        public const string OverflowLine = "\u0000overflow";

        /// <summary>
        /// The same per-run ceiling the single-run path reads stdout under (twice the log ceiling
        /// plus the result ceiling), applied to each call's own lines.
        /// </summary>
        internal const long CallOutputCeilingBytes = (2 * Ceilings.LogBytes) + Ceilings.ResultBytes;

        private static readonly TimeSpan ExitDrain = TimeSpan.FromSeconds(2);

        private readonly IReusableContainer _container;
        private readonly RunnerOptions _options;
        private readonly ILogger _logger;
        private readonly TimeProvider _time;
        private readonly SandboxOutputParser _parser = new();

        /// <summary>Module-level log lines written while the function loaded; the first call carries them.</summary>
        private SandboxOutput? _startupOutput;
        private long _lastCpuTotalMs;
        private bool _dead;

        public ReusableSandbox(IReusableContainer container, RunnerOptions options, ILogger logger, TimeProvider? time = null)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(options);
            _container = container;
            _options = options;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        public string Name => _container.Name;

        /// <summary>True once the sandbox has been killed or has died; it must not be offered again.</summary>
        public bool IsDead => _dead;

        /// <summary>Calls this sandbox has been handed.</summary>
        public int Calls { get; private set; }

        /// <summary>
        /// Starts the container and waits — under the startup allowance, as a single run's boot
        /// is — for the function to load. Anything but <c>ready</c> leaves the container killed.
        /// </summary>
        public async Task<WarmStartResult> StartAsync(CancellationToken token)
        {
            var started = Stopwatch.StartNew();
            try
            {
                return await StartCoreAsync(started, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                // Anything the Engine threw on the way — an HttpClient timeout surfaces as a
                // TaskCanceledException with nobody having cancelled anything, a socket error as
                // IOException — is the host's failure, and the container must not outlive it.
                _logger.LogWarning("Starting warm sandbox {Name} failed on the host side: {Message}", Name, ex.Message);
                await KillAsync().ConfigureAwait(false);
                return new WarmStartResult
                {
                    Status = WarmStartStatus.HostFailure,
                    StartupMs = started.ElapsedMilliseconds,
                    HostFailure = $"the warm sandbox could not be started: {ex.Message}",
                };
            }
            catch
            {
                await KillAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task<WarmStartResult> StartCoreAsync(Stopwatch started, CancellationToken token)
        {
            string? hostFailure;
            try
            {
                hostFailure = await _container.StartAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                hostFailure = ex.Message;
            }

            if (hostFailure is not null)
            {
                await KillAsync().ConfigureAwait(false);
                return new WarmStartResult
                {
                    Status = WarmStartStatus.HostFailure,
                    StartupMs = started.ElapsedMilliseconds,
                    HostFailure = hostFailure,
                };
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.StartupAllowanceSeconds));

            var output = new SandboxOutput();
            long bytes = 0;
            string? fatalCode = null;
            string? fatalMessage = null;

            try
            {
                while (true)
                {
                    var line = await _container.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                    if (line is null) break;

                    bytes += Encoding.UTF8.GetByteCount(line) + 1;
                    if (line == OverflowLine || bytes > CallOutputCeilingBytes)
                    {
                        // A function that floods stdout while it loads is not one to keep.
                        await KillAsync().ConfigureAwait(false);
                        break;
                    }

                    var evt = ProtocolLine.Read(line);
                    switch (evt.Type)
                    {
                        case "ready":
                            _startupOutput = output;
                            return new WarmStartResult { Status = WarmStartStatus.Ready, StartupMs = started.ElapsedMilliseconds };

                        case "fatal":
                            fatalCode = evt.Code;
                            fatalMessage = evt.Message;
                            break;

                        default:
                            // Module-level logs and — from an image whose bootstrap has no reuse
                            // mode — a single-run `result` line, which is how that case is told apart.
                            _parser.Feed(output, line);
                            break;
                    }

                    // A fatal ends a reuse runtime; a result line before `ready` can only come from
                    // a single-run bootstrap finishing, which exits right after it. Neither is
                    // worth waiting on any longer.
                    if (fatalCode is not null || output.Ok is not null) break;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                await KillAsync().ConfigureAwait(false);
                return new WarmStartResult
                {
                    Status = WarmStartStatus.LoadFailed,
                    StartupMs = started.ElapsedMilliseconds,
                    Output = output,
                    ExitCode = SandboxExit.Sigkill,
                    TimedOut = true,
                };
            }

            // Never ready. Whatever it was, this container is finished: killed first, so nothing
            // below can leave it running, then read for how it ended (a container that already
            // exited keeps its own exit code; the kill is a no-op for it).
            await KillAsync().ConfigureAwait(false);
            var exit = await SafeExitStateAsync().ConfigureAwait(false);

            if (IsMissingReuseRuntime(output, fatalCode, fatalMessage))
            {
                return new WarmStartResult
                {
                    Status = WarmStartStatus.NoReuseSupport,
                    StartupMs = started.ElapsedMilliseconds,
                    Output = output,
                    ExitCode = exit?.ExitCode ?? -1,
                };
            }

            if (fatalCode is not null && output.Ok is null)
            {
                // reuse.mjs reports a load failure as `fatal`; the run record wants it as the
                // failed result a single run of the same version would have produced.
                _parser.Feed(output, JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["t"] = "result",
                    ["ok"] = false,
                    ["code"] = fatalCode,
                    ["message"] = fatalMessage,
                }));
            }

            return new WarmStartResult
            {
                Status = WarmStartStatus.LoadFailed,
                StartupMs = started.ElapsedMilliseconds,
                Output = output,
                ExitCode = exit?.ExitCode ?? -1,
                OomKilled = exit?.OomKilled ?? false,
            };
        }

        /// <summary>
        /// Whether a sandbox that never said <c>ready</c> failed because the image has no reuse
        /// runtime, rather than because the function did. Both shapes of "no reuse runtime" are a
        /// bootstrap fault, <c>RUNTIME_START_FAILED</c>, reported on a single-run style
        /// <c>result</c> line: a 24-v1 bootstrap ignores the mode and cannot find execution.json;
        /// a newer bootstrap whose image lacks reuse.mjs cannot import it. A function that fails
        /// to load is a <c>fatal</c> with <c>USER_RUNTIME_ERROR</c> instead, and stays the tenant's.
        /// </summary>
        internal static bool IsMissingReuseRuntime(SandboxOutput output, string? fatalCode, string? fatalMessage)
        {
            if (output.Ok == false && output.ErrorCode == ErrorCodes.RuntimeStartFailed) return true;
            return fatalCode == ErrorCodes.RuntimeStartFailed
                && fatalMessage?.Contains("reuse.mjs", StringComparison.Ordinal) == true;
        }

        /// <summary>
        /// Serves one call: writes its envelope line, then reads until that call's <c>idle</c>.
        /// A <c>fatal</c>, an exit, a protocol violation, the output ceiling or the deadline ends
        /// the sandbox; the call still gets the most accurate outcome the evidence supports.
        /// </summary>
        /// <param name="runId">The call's id, which every line of its own carries as <c>call</c>.</param>
        /// <param name="envelopeLine">One line from <see cref="Runs.ExecutionEnvelope.ToLine"/>.</param>
        /// <param name="limits">The run's limits; the deadline comes from these.</param>
        /// <param name="startupMs">
        /// For the first call on a sandbox started for it: how long that start took, which the
        /// call reports as its startup and which comes out of the startup allowance. Null for a
        /// sandbox that was already running, whose call reports no startup at all.
        /// </param>
        /// <param name="handover">Started when the run was claimed; read when the envelope is written.</param>
        /// <param name="cancellation">The run's lease token: cancel or lease lost kills the sandbox.</param>
        public async Task<WarmCallResult> RunCallAsync(
            string runId,
            string envelopeLine,
            RunLimits limits,
            long? startupMs,
            Stopwatch? handover,
            CancellationToken cancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(envelopeLine);
            ArgumentNullException.ThrowIfNull(limits);

            Calls++;
            var firstCall = startupMs is not null;
            var output = _startupOutput ?? new SandboxOutput();
            _startupOutput = null;

            // Per call, the single-run deadline: what is left of the startup allowance (first call
            // only), the function's own timeout, and the kill grace.
            var allowanceLeftMs = firstCall
                ? Math.Max(0, (_options.StartupAllowanceSeconds * 1000L) - startupMs!.Value)
                : 0;
            var budget = TimeSpan.FromMilliseconds(
                allowanceLeftMs + ((limits.TimeoutSeconds + _options.KillGraceSeconds) * 1000L));

            var call = Stopwatch.StartNew();
            long? handoverMs = null;

            // The write has its own short deadline. A tenant that wedged the event loop at module
            // level (a synchronous loop in a timer, say) stops reading stdin; once the pipe is
            // full the write would block for ever, and with it the run loop of this whole runner.
            using (var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                writeDeadline.CancelAfter(TimeSpan.FromMilliseconds(_options.WarmWriteTimeoutMs));
                try
                {
                    await _container.WriteLineAsync(envelopeLine, writeDeadline.Token).ConfigureAwait(false);
                    handoverMs = handover?.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    // Every failure here, a cancellation included, becomes a result: an exception
                    // escaping a call is what used to end the run loop.
                    var cancelled = cancellation.IsCancellationRequested;
                    _logger.LogWarning("Could not hand call {RunId} to warm sandbox {Name}: {Message}",
                        runId, Name, cancelled ? "the run was cancelled" : ex.Message);
                    await KillAsync().ConfigureAwait(false);
                    return new WarmCallResult
                    {
                        Result = new SandboxResult
                        {
                            Output = output,
                            ExitCode = -1,
                            OomKilled = false,
                            TimedOut = false,
                            DurationMs = call.ElapsedMilliseconds,
                            HostFailure = cancelled ? null : "the warm sandbox did not accept the call",
                        },
                        Discard = cancelled ? "cancelled" : ex is OperationCanceledException ? "timeout" : "crash",
                        Started = false,
                    };
                }
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(budget);

            long bytes = 0;
            long? handlerStartedMs = null;
            long? answeredMs = null;
            string? discard = null;
            var timedOut = false;
            var idle = false;
            var clean = false;
            var late = false;
            IReadOnlyList<string> leftovers = [];
            long? handlerCpuMs = null;
            long? hostCpuAtStart = null, hostCpuAtAnswer = null;
            var ended = false;   // the stream ended: the sandbox exited

            try
            {
                while (!idle && discard is null)
                {
                    var line = await _container.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        ended = true;
                        discard = "crash";
                        break;
                    }

                    bytes += Encoding.UTF8.GetByteCount(line) + 1;
                    if (line == OverflowLine || bytes > CallOutputCeilingBytes)
                    {
                        // The out-of-process ceiling. A single run stops reading and lets the
                        // sandbox finish; a sandbox that is to serve again cannot be left with an
                        // unread pipe, so it is ended instead.
                        output.Truncated = true;
                        output.TruncationReason ??= "log_bytes";
                        discard = "protocol";
                        break;
                    }

                    var evt = ProtocolLine.Read(line);
                    switch (evt.Type)
                    {
                        case "log" or "truncated" or "started" or "result":
                            if (evt.Late || !string.Equals(evt.Call, runId, StringComparison.Ordinal))
                            {
                                // An earlier call's code, or the module's own background. Its
                                // run is already recorded; it is not this caller's output. The
                                // runtime counts it against this call's cleanliness itself.
                                break;
                            }
                            if (evt.Type == "started")
                            {
                                handlerStartedMs ??= call.ElapsedMilliseconds;
                                hostCpuAtStart ??= _container.HostCpuMicroseconds();
                            }
                            if (evt.Type == "result")
                            {
                                answeredMs ??= call.ElapsedMilliseconds;
                                hostCpuAtAnswer ??= _container.HostCpuMicroseconds();
                            }
                            _parser.Feed(output, line);
                            break;

                        case "idle":
                            if (!string.Equals(evt.Call, runId, StringComparison.Ordinal))
                            {
                                discard = "protocol";
                                break;
                            }
                            idle = true;
                            clean = evt.Clean;
                            late = evt.Late;
                            leftovers = evt.Leftovers;
                            handlerCpuMs = evt.CpuMs;
                            if (!clean) discard = DirtyReason(leftovers, late);
                            break;

                        case "fatal":
                            // The runtime is exiting. A timeout it caught itself is a timeout;
                            // anything else is a crash.
                            discard = evt.Code == ErrorCodes.TimedOut ? "timeout" : "crash";
                            break;

                        case "ready":
                            discard = "protocol";
                            break;

                        default:
                            // Not protocol JSON: a package writing to fd 1 directly. Recorded as
                            // malformed, exactly as for a single run, and harmless.
                            _parser.Feed(output, line);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                timedOut = true;
                discard = "timeout";
                _logger.LogWarning(
                    "Call {RunId} on warm sandbox {Name} exceeded {Budget}s — killing the sandbox",
                    runId, Name, (int)budget.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                discard = "cancelled";
                _logger.LogInformation("Call {RunId} was cancelled — killing warm sandbox {Name}", runId, Name);
            }
            catch (Exception ex)
            {
                // The reader never throws by contract; if it does, the sandbox is not trusted.
                discard = "protocol";
                _logger.LogWarning("Reading call {RunId} from warm sandbox {Name} failed: {Message}", runId, Name, ex.Message);
            }

            call.Stop();

            var exitCode = 0;
            var oomKilled = false;
            long? memoryBytes = null;
            long? cpuUsageMs = null;

            if (discard is null)
            {
                // Still alive, and clean so far: measure it — the pool decides on memory.
                (memoryBytes, cpuUsageMs) = await MeasureAsync().ConfigureAwait(false);

                // The runtime measures its own CPU over exactly the handler's window — the window
                // the reported duration covers. Docker's figure is the container's total since the
                // last call (unpause, envelope read, clean-up check included), so dividing it by the
                // handler's duration overstated a warm call ("144 / 100 m"). Docker's stays the
                // fallback for a runtime that does not report one.
                if (handlerCpuMs is { } exact) cpuUsageMs = Math.Max(1, exact);

                // Better still, the host's own counter over the same window: it is what the limit
                // is enforced against, so it is the figure that can be compared with the limit.
                // The runtime's under gVisor includes time spent throttled (HostCpuMicroseconds).
                if (hostCpuAtStart is { } from && hostCpuAtAnswer is { } to && to >= from)
                {
                    cpuUsageMs = Math.Max(1, (to - from + 500) / 1000);
                }
            }
            else
            {
                // Killed before it is inspected: the deadline is the one that holds, and nothing a
                // sandbox does after its verdict may be waited on.
                if (!ended) await KillAsync().ConfigureAwait(false);
                else _dead = true;

                var exit = await SafeExitStateAsync().ConfigureAwait(false);
                exitCode = exit?.ExitCode ?? -1;
                oomKilled = exit?.OomKilled ?? false;
                if (oomKilled) discard = "memory";
            }

            // The single-run split, measured on the runner's clock from the protocol lines.
            var handlerAt = handlerStartedMs ?? call.ElapsedMilliseconds;
            var answerAt = answeredMs ?? call.ElapsedMilliseconds;
            var reportedStartup = firstCall ? startupMs!.Value + handlerAt : 0;
            var executionMs = Math.Max(0, answerAt - handlerAt);

            return new WarmCallResult
            {
                Result = new SandboxResult
                {
                    Output = output,
                    ExitCode = exitCode,
                    OomKilled = oomKilled,
                    TimedOut = timedOut,
                    DurationMs = (firstCall ? startupMs!.Value : 0) + answerAt,
                    StartupMs = handlerStartedMs is null && !firstCall ? null : reportedStartup,
                    ExecutionMs = handlerStartedMs is null ? null : executionMs,
                    PeakMemoryBytes = memoryBytes,
                    CpuUsageMs = cpuUsageMs,
                },
                Clean = clean,
                Leftovers = leftovers,
                Late = late,
                Discard = discard,
                HandoverMs = handoverMs,
                MemoryBytes = memoryBytes,
                Started = handlerStartedMs is not null,
            };
        }

        /// <summary>How the container ended, or null — never an exception: it is read on failure paths.</summary>
        private async Task<(int ExitCode, bool OomKilled)?> SafeExitStateAsync()
        {
            try
            {
                return await _container.ExitStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read how warm sandbox {Name} ended: {Message}", Name, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// <c>dirty:</c> plus what was left, as the runtime named it, joined by commas (e.g.
        /// <c>dirty:Timeout,fetch x1</c> — the shape the console parses) — or the reason the
        /// runtime itself gave for a call it ended.
        /// </summary>
        internal static string DirtyReason(IReadOnlyList<string> leftovers, bool late)
        {
            // The runtime reports its own fatal ends as an idle with a one-word leftover first.
            if (leftovers is ["timeout"] or ["waitUntil"]) return "timeout";
            if (leftovers is ["crash"]) return "crash";

            // A comma inside a name would split it in the console; the runtime never sends one.
            var parts = leftovers.Select(l => l.Replace(',', ' ')).ToList();
            if (late) parts.Add("late");
            return parts.Count == 0 ? "dirty:unknown" : "dirty:" + string.Join(',', parts);
        }

        /// <summary>
        /// Memory now and CPU used since the last measurement. The CPU counter is cumulative over
        /// the container's life, so the first call's share includes the module import — the same
        /// thing a single run's CPU time includes.
        /// </summary>
        private async Task<(long? MemoryBytes, long? CpuUsageMs)> MeasureAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var (memory, cpuTotal) = await _container.StatsAsync(cts.Token).ConfigureAwait(false);
                long? cpu = null;
                if (cpuTotal is { } total && total >= _lastCpuTotalMs)
                {
                    cpu = Math.Max(1, total - _lastCpuTotalMs);
                    _lastCpuTotalMs = total;
                }
                return (memory, cpu);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read stats for warm sandbox {Name}: {Message}", Name, ex.Message);
                return (null, null);
            }
        }

        public async Task<bool> PauseAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _container.PauseAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not pause warm sandbox {Name}: {Message}", Name, ex.Message);
                return false;
            }
        }

        public async Task<bool> UnpauseAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _container.UnpauseAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not unpause warm sandbox {Name}: {Message}", Name, ex.Message);
                return false;
            }
        }

        private async Task KillAsync()
        {
            _dead = true;
            try
            {
                await _container.KillAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Killing warm sandbox {Name} failed: {Message}", Name, ex.Message);
            }
        }

        /// <summary>Kills and removes the container. Safe to call more than once.</summary>
        public async ValueTask DisposeAsync()
        {
            _dead = true;
            try
            {
                await _container.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not remove warm sandbox {Name}: {Message}", Name, ex.Message);
            }
        }

        /// <summary>The fields of one reuse-protocol line this side acts on.</summary>
        internal readonly record struct ProtocolLine(
            string? Type, string? Call, bool Late, bool Clean, IReadOnlyList<string> Leftovers, string? Code, string? Message,
            long? CpuMs = null)
        {
            /// <summary>Reads a line; anything that is not a JSON object with a string <c>t</c> has a null type.</summary>
            public static ProtocolLine Read(string line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return Empty;

                    string? Str(string name) =>
                        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    bool Bool(string name) =>
                        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

                    var leftovers = new List<string>();
                    if (root.TryGetProperty("leftovers", out var l) && l.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in l.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String && leftovers.Count < 32) leftovers.Add(item.GetString()!);
                        }
                    }

                    long? cpuMs = root.TryGetProperty("cpuMs", out var c) && c.ValueKind == JsonValueKind.Number
                        && c.TryGetInt64(out var cpu) && cpu >= 0
                        ? cpu
                        : null;

                    return new ProtocolLine(
                        Str("t"), Str("call"), Bool("late"), Bool("clean"), leftovers, Str("code"), Str("message"), cpuMs);
                }
                catch (JsonException)
                {
                    return Empty;
                }
            }

            private static readonly ProtocolLine Empty = new(null, null, false, false, [], null, null);
        }
    }

    /// <summary>
    /// <see cref="IReusableContainer"/> over the Docker Engine: the profile from
    /// <see cref="SandboxProfile.CreateReusable"/>, verified before start exactly as a run's is,
    /// with stdin attached for the calls and a background reader turning the multiplexed output
    /// into lines.
    /// </summary>
    public sealed class DockerReusableContainer : IReusableContainer
    {
        /// <summary>
        /// The longest single line kept: a result line carries up to the 5 MB result ceiling plus
        /// a little framing. Anything longer is replaced by <see cref="ReusableSandbox.OverflowLine"/>.
        /// </summary>
        internal const int MaxLineChars = (int)Ceilings.ResultBytes + (64 * 1024);

        private readonly IDockerClient _docker;
        private readonly string _image;
        private readonly RunLimits _limits;
        private readonly RunnerOptions _options;
        private readonly ILogger _logger;

        // Bounded, so a sandbox writing faster than the runner reads is slowed by the pipe rather
        // than buffered without limit in the runner's memory.
        private readonly Channel<string> _lines = Channel.CreateBounded<string>(
            new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

        private readonly CancellationTokenSource _pumpCts = new();
        private MultiplexedStream? _stream;
        private Task? _pump;
        private string? _containerId;
        private int _disposed;

        public DockerReusableContainer(
            IDockerClient docker, string name, string image, RunLimits limits, RunnerOptions options, ILogger logger)
        {
            _docker = docker;
            Name = name;
            _image = image;
            _limits = limits;
            _options = options;
            _logger = logger;
        }

        public string Name { get; }

        public async Task<string?> StartAsync(CancellationToken token)
        {
            var parameters = SandboxProfile.CreateReusable(Name, _image, _limits, _options);
            try
            {
                var created = await _docker.Containers.CreateContainerAsync(parameters, token).ConfigureAwait(false);
                _containerId = created.ID;
            }
            catch (DockerApiException ex)
            {
                return $"the sandbox could not be created: {ex.Message}";
            }

            var inspect = await _docker.Containers.InspectContainerAsync(_containerId, token).ConfigureAwait(false);
            var discrepancy = SandboxProfile.Validate(inspect, _limits, _options, reuse: true);
            if (discrepancy is not null)
            {
                _logger.LogError(
                    "Refusing to start warm sandbox {Name}: the security profile was not applied — {Discrepancy}",
                    Name, discrepancy);
                return $"the security profile was not applied: {discrepancy}";
            }

            _stream = await _docker.Containers.AttachContainerAsync(
                _containerId,
                tty: false,
                new ContainerAttachParameters { Stream = true, Stdin = true, Stdout = true, Stderr = true },
                _pumpCts.Token).ConfigureAwait(false);

            var started = await _docker.Containers.StartContainerAsync(
                _containerId, new ContainerStartParameters(), token).ConfigureAwait(false);
            if (!started) return "the sandbox did not start";

            _pump = Task.Run(() => PumpAsync(_stream, _pumpCts.Token), CancellationToken.None);
            return null;
        }

        /// <summary>
        /// Reads the attach stream into lines until it ends. Decoded statefully (a character split
        /// across two reads survives) and capped per line, as <see cref="CappedOutputReader"/> does
        /// for a whole run.
        /// </summary>
        private async Task PumpAsync(MultiplexedStream stream, CancellationToken token)
        {
            var decoder = new UTF8Encoding(false, false).GetDecoder();
            var buffer = new byte[16 * 1024];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length + 3)];
            var line = new StringBuilder();
            var overflow = false;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read.EOF) break;
                    if (read.Count <= 0) continue;

                    var count = decoder.GetChars(buffer, 0, read.Count, chars, 0, flush: false);
                    for (var i = 0; i < count; i++)
                    {
                        var c = chars[i];
                        if (c == '\n')
                        {
                            var text = overflow ? ReusableSandbox.OverflowLine : line.ToString();
                            line.Clear();
                            overflow = false;
                            await _lines.Writer.WriteAsync(text, token).ConfigureAwait(false);
                            continue;
                        }

                        if (overflow) continue;
                        if (line.Length >= MaxLineChars)
                        {
                            overflow = true;
                            line.Clear();
                            continue;
                        }
                        line.Append(c);
                    }
                }

                if (line.Length > 0 || overflow)
                {
                    await _lines.Writer.WriteAsync(overflow ? ReusableSandbox.OverflowLine : line.ToString(), token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                _logger.LogDebug("Reading warm sandbox {Name} ended: {Message}", Name, ex.Message);
            }
            finally
            {
                _lines.Writer.TryComplete();
            }
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            while (await _lines.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                if (_lines.Reader.TryRead(out var line)) return line;
            }
            return null;
        }

        public async Task WriteLineAsync(string line, CancellationToken token)
        {
            if (_stream is null) throw new InvalidOperationException("the warm sandbox is not attached");
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await _stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
        }

        public Task PauseAsync(CancellationToken token) =>
            _docker.Containers.PauseContainerAsync(RequireId(), token);

        public Task UnpauseAsync(CancellationToken token) =>
            _docker.Containers.UnpauseContainerAsync(RequireId(), token);

        /// <inheritdoc />
        public long? HostCpuMicroseconds()
        {
            if (_containerId is null) return null;
            // systemd cgroup driver (this host), then the cgroupfs driver's layout.
            foreach (var path in (string[])[
                $"/sys/fs/cgroup/system.slice/docker-{_containerId}.scope/cpu.stat",
                $"/sys/fs/cgroup/docker/{_containerId}/cpu.stat"])
            {
                try
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        if (line.StartsWith("usage_usec ", StringComparison.Ordinal)
                            && long.TryParse(line.AsSpan(11), System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out var usec))
                        {
                            return usec;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Not this layout, or not readable: try the next, else fall back.
                }
            }
            return null;
        }

        public async Task<(long? MemoryBytes, long? CpuTotalMs)> StatsAsync(CancellationToken token)
        {
            ContainerStatsResponse? last = null;
            await _docker.Containers.GetContainerStatsAsync(
                RequireId(),
                new ContainerStatsParameters { Stream = false, OneShot = true },
                new Progress<ContainerStatsResponse>(s => last = s),
                token).ConfigureAwait(false);

            // Progress<T> posts to the thread pool; give the one callback a moment to land.
            for (var i = 0; i < 20 && last is null; i++) await Task.Delay(5, token).ConfigureAwait(false);
            if (last is null) return (null, null);

            var memory = WorkingSetBytes(last.MemoryStats);
            long? cpu = last.CPUStats?.CPUUsage?.TotalUsage is > 0 ? (long)(last.CPUStats.CPUUsage.TotalUsage / 1_000_000) : null;
            return (memory, cpu);
        }

        /// <summary>
        /// The memory a sandbox actually holds: usage minus the inactive file cache, which the
        /// kernel drops before it would OOM-kill anything — the same figure <c>docker stats</c>
        /// shows. Raw usage counts that cache, so a function that reads files would look "full"
        /// and be recycled for memory it does not hold. cgroup v2 names it <c>inactive_file</c>,
        /// v1 <c>total_inactive_file</c>.
        /// </summary>
        internal static long? WorkingSetBytes(MemoryStats? stats)
        {
            if (stats is null || stats.Usage == 0) return null;
            ulong inactive = 0;
            if (stats.Stats is { } detail &&
                (detail.TryGetValue("inactive_file", out inactive) || detail.TryGetValue("total_inactive_file", out inactive)) &&
                inactive < stats.Usage)
            {
                return (long)(stats.Usage - inactive);
            }
            return (long)stats.Usage;
        }

        public async Task KillAsync()
        {
            if (_containerId is null) return;
            try
            {
                await _docker.Containers.KillContainerAsync(
                    _containerId, new ContainerKillParameters { Signal = "SIGKILL" }, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Already gone — or paused: SIGKILL reaches a paused container too, and the
                // removal in DisposeAsync is forced either way. Any other failure (the Engine not
                // answering) is left to that forced removal and, failing that, to the reaper.
            }
        }

        public async Task<(int ExitCode, bool OomKilled)?> ExitStateAsync()
        {
            if (_containerId is null) return null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _docker.Containers.WaitContainerAsync(_containerId, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Still going down; read whatever state it has.
                }

                var inspect = await _docker.Containers.InspectContainerAsync(_containerId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (inspect.State?.Running == true) return null;
                return ((int)(inspect.State?.ExitCode ?? -1), inspect.State?.OOMKilled ?? false);
            }
            catch (Exception)
            {
                // Read on failure paths only: an unreadable state is "unknown", never a new failure.
                return null;
            }
        }

        private string RequireId() =>
            _containerId ?? throw new InvalidOperationException("the warm sandbox was never created");

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            await _pumpCts.CancelAsync().ConfigureAwait(false);
            if (_containerId is not null)
            {
                try
                {
                    await _docker.Containers.RemoveContainerAsync(
                        _containerId,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The reaper finds it by its label once it is no longer in any pool.
                    _logger.LogWarning("Could not remove warm sandbox {Name}: {Message}", Name, ex.Message);
                }
            }

            if (_pump is not null)
            {
                try { await _pump.ConfigureAwait(false); } catch (Exception) { /* ended either way */ }
            }
            _stream?.Dispose();
            _pumpCts.Dispose();
        }
    }
}
