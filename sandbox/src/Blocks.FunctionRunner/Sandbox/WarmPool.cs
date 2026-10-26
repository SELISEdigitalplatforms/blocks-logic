using System.Diagnostics;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// What a warm sandbox may be reused for: one tenant's one version of one function, from one
    /// image. Never across any of them — a sandbox carries module state from every call it served,
    /// and that state is only ever the same function author's.
    /// </summary>
    public sealed record WarmKey(string TenantId, string FunctionId, string VersionId, string Image);

    /// <summary>Makes the sandboxes the pool keeps; replaced in tests.</summary>
    public interface IReusableSandboxFactory
    {
        ReusableSandbox Create(WarmKey key, RunLimits limits);
    }

    /// <inheritdoc cref="IReusableSandboxFactory"/>
    public sealed class DockerReusableSandboxFactory : IReusableSandboxFactory
    {
        private readonly IDockerClient _docker;
        private readonly RunnerOptions _options;
        private readonly ILoggerFactory _loggers;

        public DockerReusableSandboxFactory(IDockerClient docker, IOptions<RunnerOptions> options, ILoggerFactory loggers)
        {
            _docker = docker;
            _options = options.Value;
            _loggers = loggers;
        }

        public ReusableSandbox Create(WarmKey key, RunLimits limits)
        {
            ArgumentNullException.ThrowIfNull(key);

            // Random, not derived from the key: two sandboxes of one version are normal, and the
            // name must not tell anyone on the host whose function it is.
            var name = SandboxProfile.WarmContainerPrefix + Guid.NewGuid().ToString("N");
            var logger = _loggers.CreateLogger<ReusableSandbox>();
            return new ReusableSandbox(
                new DockerReusableContainer(_docker, name, key.Image, limits, _options, logger), _options, logger);
        }
    }

    /// <summary>The outcome of <see cref="WarmPool.AcquireAsync"/>.</summary>
    public enum WarmAcquireStatus
    {
        /// <summary>A sandbox is the caller's for one call.</summary>
        Acquired,

        /// <summary>No room: CPU or RAM for a start, three starts in flight, or the function's cap. The run goes back to the queue.</summary>
        NoCapacity,

        /// <summary>The image has no reuse runtime; serve the run the single-run way.</summary>
        NoReuseSupport,

        /// <summary>A new sandbox failed to start; <see cref="WarmAcquireResult.Start"/> says how.</summary>
        StartFailed,
    }

    /// <summary>What <see cref="WarmPool.AcquireAsync"/> returned.</summary>
    public sealed record WarmAcquireResult(WarmAcquireStatus Status, WarmHandle? Handle = null, WarmStartResult? Start = null);


    /// <summary>
    /// One sandbox lent to one caller for one call. Give it back through the pool — exactly once:
    /// the pool ignores a second hand-back of the same handle, so a failure path that hands it back
    /// again cannot destroy a sandbox already lent to someone else.
    /// </summary>
    public sealed class WarmHandle
    {
        private int _finished;

        internal WarmHandle(WarmPool.Entry entry, bool reused, long? startupMs)
        {
            Entry = entry;
            Reused = reused;
            StartupMs = startupMs;
            Paused = reused;
        }

        internal WarmPool.Entry Entry { get; }

        /// <summary>True when the sandbox was already running before this call (the result's <c>reused=1</c>).</summary>
        public bool Reused { get; }

        /// <summary>For a sandbox started for this call: how long the start took. Null otherwise.</summary>
        public long? StartupMs { get; }

        /// <summary>
        /// Still paused. A sandbox from the pool stays paused until the very moment its call is
        /// handed over — after the run's secrets and token are resolved — so it runs nothing while
        /// the caller prepares, and a run that turns out not to be runnable goes back untouched.
        /// </summary>
        internal bool Paused { get; private set; }

        /// <summary>The sandbox's runtime asks for the caller's token on demand (see ReusableSandbox.CanAsk).</summary>
        public bool CanAsk => Entry.Sandbox.CanAsk;

        public ReusableSandbox Sandbox => Entry.Sandbox;

        /// <summary>True the first time only.</summary>
        internal bool TryFinish() => Interlocked.Exchange(ref _finished, 1) == 0;

        /// <summary>
        /// Resumes the sandbox if it is paused, then serves the call. The first call on a sandbox
        /// started for it carries the startup. A sandbox that will not resume is reported as a call
        /// that never started, so the caller may serve the run elsewhere.
        /// </summary>
        public async Task<WarmCallResult> RunCallAsync(
            string runId, string envelopeLine, RunLimits limits, Stopwatch? handover, CancellationToken token,
            Func<CancellationToken, Task<string?>>? accessToken = null, Action<Protocol.SandboxOutput>? onAnswer = null,
            Action<string>? onChunk = null)
        {
            if (Paused)
            {
                if (!await Entry.Sandbox.UnpauseAsync().ConfigureAwait(false))
                {
                    return new WarmCallResult
                    {
                        Result = new SandboxResult
                        {
                            Output = new Protocol.SandboxOutput(),
                            ExitCode = -1,
                            OomKilled = false,
                            TimedOut = false,
                            DurationMs = 0,
                            HostFailure = "the warm sandbox could not be resumed",
                        },
                        Discard = "protocol",
                        Started = false,
                    };
                }
                Paused = false;
            }

            return await Entry.Sandbox.RunCallAsync(runId, envelopeLine, limits, StartupMs, handover, token, accessToken, onAnswer, onChunk)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The warm sandboxes this runner keeps (sandbox/REUSE.md).
    /// <para>
    /// The rules, in the order they bite: a sandbox serves one key only; one call at a time; after
    /// a call it goes back paused only if the runtime called the call clean and none of the limits
    /// — calls, age, memory — is reached; anything else destroys it with the reason recorded on the
    /// run. Idle sandboxes are removed by <see cref="SweepAsync"/> (RUNNER-FLOW step 6). Nothing is
    /// started ahead of a call: the first call of a version warms it (step 2), and nothing is warmed
    /// again after a restart (step 0.7). No RAM is booked: a removed sandbox's RAM is free at once.
    /// </para>
    /// <para>
    /// Locking: one lock over the bookkeeping, never held across a Docker call. A sandbox is
    /// claimed (marked busy) under the lock before anything slow happens to it, so two callers can
    /// never both get it, and the sweep never touches a busy one. A start reserves its place in the
    /// function's count under the same lock as the count is checked, so concurrent starts cannot
    /// overshoot the cap. The count is of the live sandboxes themselves, never a separate number.
    /// Every path that cannot finish — an exception included — ends in <see cref="DestroyAsync"/>,
    /// so no entry or container is left behind.
    /// </para>
    /// </summary>
    public sealed class WarmPool : IAsyncDisposable
    {
        private readonly IReusableSandboxFactory _factory;
        private readonly HostRoom _room;
        private readonly RunnerOptions _options;
        private readonly ILogger<WarmPool> _logger;
        private readonly TimeProvider _time;

        private readonly Lock _gate = new();
        private readonly Dictionary<WarmKey, List<Entry>> _entries = [];

        /// <summary>Starts in flight per key, counted against the version's cap before their entry exists.</summary>
        private readonly Dictionary<WarmKey, int> _starting = [];

        /// <summary>
        /// Images known to have no reuse runtime (built FROM 24-v1). Remembered for the life of the
        /// process: an image is immutable, so the answer cannot change, and asking again would cost
        /// a failed sandbox start per run.
        /// </summary>
        private readonly HashSet<string> _noReuse = new(StringComparer.Ordinal);

        /// <summary>
        /// Versions whose module failed to load by its own fault, and until when they are not
        /// started again (<see cref="RunnerOptions.FailedLoadHoldSeconds"/>). The same code fails
        /// the same way, so a start per call only burned a start's CPU for the same answer.
        /// </summary>
        private readonly Dictionary<WarmKey, (long Until, WarmStartResult Start)> _heldLoads = [];

        private bool _disposed;

        public WarmPool(
            IReusableSandboxFactory factory,
            HostRoom room,
            IOptions<RunnerOptions> options,
            ILogger<WarmPool> logger,
            TimeProvider? time = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            _factory = factory;
            _room = room;
            _options = options.Value;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>One sandbox and its bookkeeping. Mutated only under the pool's lock.</summary>
        internal sealed class Entry
        {
            public required WarmKey Key { get; init; }
            public required ReusableSandbox Sandbox { get; init; }
            public required RunLimits Limits { get; init; }
            public required long CreatedTicks { get; init; }
            public long LastUsedTicks { get; set; }
            public int Calls { get; set; }
            public bool Busy { get; set; }

            /// <summary>When it was last lent to a call (or created for one) — for the hung-call clean-up.</summary>
            public long LentTicks { get; set; }
            public bool Draining { get; set; }
            public bool Removed { get; set; }
        }

        /// <summary>Warm sandboxes alive: total, serving a call (or starting), idle (paused). For the heartbeat only.</summary>
        public (int Total, int Busy, int Idle) Counts
        {
            get
            {
                lock (_gate)
                {
                    var all = _entries.Values.SelectMany(e => e).ToList();
                    var busy = all.Count(e => e.Busy);
                    return (all.Count, busy, all.Count - busy);
                }
            }
        }

        /// <summary>True when some sandbox is idle (paused, ready for a call) — the run loop's test for "the next run may be a warm call".</summary>
        public bool HasIdle
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Values.Any(list => list.Any(e => !e.Busy && !e.Draining && !e.Removed && !e.Sandbox.IsDead));
                }
            }
        }

        /// <summary>
        /// Whether a sandbox of this key is alive here (busy or not). If so its image is on this
        /// host — Docker will not remove an image a container uses — so a warm call need not ask
        /// Docker for it again (one engine round trip per call, ~40 ms measured).
        /// </summary>
        public bool HasLive(WarmKey key)
        {
            lock (_gate)
            {
                return _entries.TryGetValue(key, out var list) && list.Any(e => !e.Removed);
            }
        }

        /// <summary>False once this image has shown it cannot run in reuse mode.</summary>
        public bool SupportsReuse(string image)
        {
            lock (_gate) { return !_noReuse.Contains(image); }
        }

        /// <summary>
        /// The function's own load error: its module threw, so it says so the same way every time.
        /// Not a timeout, an OOM kill or a crash — those can pass (a slow dependency, a busy host),
        /// so the next call tries again.
        /// </summary>
        internal static bool IsOwnLoadFailure(WarmStartResult start) =>
            start.Status == WarmStartStatus.LoadFailed && !start.TimedOut && !start.OomKilled
            && start.Output.ErrorCode == ErrorCodes.UserRuntimeError;

        private void Hold(WarmKey key, WarmStartResult start)
        {
            if (_options.FailedLoadHoldSeconds <= 0) return;
            var now = _time.GetTimestamp();
            var until = now + (long)(_options.FailedLoadHoldSeconds * (double)_time.TimestampFrequency);
            lock (_gate)
            {
                // Expired holds go here, so the map holds only versions failing right now.
                foreach (var stale in _heldLoads.Where(h => h.Value.Until <= now).Select(h => h.Key).ToList())
                {
                    _heldLoads.Remove(stale);
                }
                _heldLoads[key] = (until, start);
            }
            _logger.LogWarning(
                "Function {FunctionId} version {VersionId} failed to load; not started again for {Seconds}s",
                key.FunctionId, key.VersionId, _options.FailedLoadHoldSeconds);
        }

        /// <summary>
        /// The failed start to answer with while this version is held, or null. Answered as a start
        /// that took no time: no sandbox was made for this call.
        /// </summary>
        private WarmStartResult? HeldLoad(WarmKey key)
        {
            lock (_gate)
            {
                if (!_heldLoads.TryGetValue(key, out var held)) return null;
                if (held.Until > _time.GetTimestamp()) return held.Start with { StartupMs = 0 };
                _heldLoads.Remove(key);
                return null;
            }
        }

        /// <summary>
        /// Removes every sandbox lent to a call (or starting for one) for longer than
        /// <paramref name="limit"/> — no call may last that long, so it is hung (RUNNER-FLOW step 9.2).
        /// The call holding it ends as a crash and reports; the sandbox's CPU and RAM are free at once.
        /// </summary>
        /// <returns>How many were removed.</returns>
        public async Task<int> RemoveHungAsync(TimeSpan limit)
        {
            List<Entry> hung;
            lock (_gate)
            {
                var now = _time.GetTimestamp();
                hung = [.. _entries.Values.SelectMany(list => list)
                    .Where(e => e.Busy && !e.Removed && e.LentTicks != 0 && _time.GetElapsedTime(e.LentTicks, now) >= limit)];
            }
            foreach (var entry in hung) await DestroyAsync(entry, "hung").ConfigureAwait(false);
            return hung.Count;
        }

        /// <summary>
        /// The clean-up found this container dead in Docker (exited, not paused): the idle sandbox the
        /// pool keeps under that name is removed now, so it no longer holds a place under its
        /// function's cap (RUNNER-FLOW step 5.6). A busy one is left to its call, which ends it.
        /// </summary>
        /// <returns>True when an idle sandbox was removed.</returns>
        public async Task<bool> RemoveDeadAsync(string containerName)
        {
            Entry? dead;
            lock (_gate)
            {
                dead = _entries.Values.SelectMany(list => list)
                    .FirstOrDefault(e => e.Sandbox.Name == containerName && !e.Busy && !e.Removed);
                if (dead is not null) dead.Busy = true;
            }
            if (dead is null) return false;
            await DestroyAsync(dead, "crash").ConfigureAwait(false);
            return true;
        }

        /// <summary>Whether a container of this name is one the pool knows about — the reaper's test.</summary>
        public bool IsTracked(string containerName)
        {
            lock (_gate)
            {
                return _entries.Values.Any(list => list.Any(e => e.Sandbox.Name == containerName));
            }
        }

        /// <summary>
        /// The most sandboxes one function may have here (RUNNER-FLOW step 5.6): its own concurrency
        /// setting — 3 by default, up to 10 — unless the host pins a number.
        /// </summary>
        private int MaxPerFunction(RunLimits limits) =>
            _options.WarmMaxPerVersion > 0 ? _options.WarmMaxPerVersion : Math.Max(1, limits.FunctionConcurrency);

        /// <summary>
        /// The function's sandboxes that count against its cap: the real ones, alive and serving it.
        /// A dead one or one being drained (an old version) does not count. Under the lock.
        /// </summary>
        private int LiveCountLocked(WarmKey key) =>
            _entries
                .Where(kv => kv.Key.TenantId == key.TenantId && kv.Key.FunctionId == key.FunctionId)
                .Sum(kv => kv.Value.Count(e => !e.Removed && !e.Draining && !e.Sandbox.IsDead))
            + _starting.Where(kv => kv.Key.TenantId == key.TenantId && kv.Key.FunctionId == key.FunctionId).Sum(kv => kv.Value);

        /// <summary>Idle sandboxes found dead, taken for removal. Under the lock.</summary>
        private List<Entry> TakeDeadIdleLocked() =>
            [.. _entries.Values.SelectMany(e => e).Where(e => !e.Busy && !e.Removed && e.Sandbox.IsDead).Select(e => { e.Busy = true; return e; })];

        /// <summary>
        /// A sandbox for one call: an idle one of this key — still paused, see
        /// <see cref="WarmHandle.Paused"/> — or a new one, started. The caller must hand it back
        /// with <see cref="ReleaseAsync"/>, <see cref="ReturnUnusedAsync"/> or <see cref="DiscardAsync"/>.
        /// </summary>
        /// <param name="allowIdle">False to insist on a fresh sandbox — the retry after a reused one died.</param>
        public async Task<WarmAcquireResult> AcquireAsync(
            WarmKey key, RunLimits limits, CancellationToken token, bool allowIdle = true)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(limits);

            if (!SupportsReuse(key.Image)) return new WarmAcquireResult(WarmAcquireStatus.NoReuseSupport);
            if (HeldLoad(key) is { } held) return new WarmAcquireResult(WarmAcquireStatus.StartFailed, Start: held);

            // Dead ones are removed when found, so they never hold a place under the cap.
            List<Entry> dead;
            lock (_gate) { dead = TakeDeadIdleLocked(); }
            foreach (var entry in dead) await DestroyAsync(entry, "crash").ConfigureAwait(false);

            if (allowIdle)
            {
                lock (_gate)
                {
                    if (_disposed) return new WarmAcquireResult(WarmAcquireStatus.NoCapacity);
                    var idle = _entries.TryGetValue(key, out var list)
                        ? list
                            .Where(e => !e.Busy && !e.Draining && !e.Removed && !e.Sandbox.IsDead && e.Limits == limits)
                            .OrderByDescending(e => e.LastUsedTicks)   // the warmest: its caches are freshest
                            .FirstOrDefault()
                        : null;
                    if (idle is not null)
                    {
                        idle.Busy = true;
                        idle.LentTicks = _time.GetTimestamp();
                        return new WarmAcquireResult(WarmAcquireStatus.Acquired, new WarmHandle(idle, reused: true, startupMs: null));
                    }
                }
            }

            return await StartNewAsync(key, limits, token).ConfigureAwait(false);
        }

        /// <summary>Starts one sandbox for <paramref name="key"/>, for the caller (the first call warms it).</summary>
        private async Task<WarmAcquireResult> StartNewAsync(WarmKey key, RunLimits limits, CancellationToken token)
        {
            // --- a place under the function's cap, taken under the lock that counts --------
            lock (_gate)
            {
                if (_disposed) return new WarmAcquireResult(WarmAcquireStatus.NoCapacity);
                if (LiveCountLocked(key) >= MaxPerFunction(limits)) return new WarmAcquireResult(WarmAcquireStatus.NoCapacity);
                _starting[key] = _starting.GetValueOrDefault(key) + 1;
            }

            Entry? entry = null;
            ColdStart? start = null;
            try
            {
                // A start's room, read live (RUNNER-FLOW step 0): the boost CPU, the sandbox's RAM,
                // and a cold-start place. Nothing is booked for the sandbox's life — once it is gone
                // its RAM is free at once. Only when RAM is what is short is an idle sandbox of some
                // other version worth giving up for this one.
                start = _room.TryStartCold(limits.MemoryBytes, out var refusal);
                if (start is null && refusal == RoomRefusal.Memory && await EvictIdleAsync(except: key).ConfigureAwait(false))
                {
                    start = _room.TryStartCold(limits.MemoryBytes, out _);
                }
                if (start is null) return new WarmAcquireResult(WarmAcquireStatus.NoCapacity);

                var now = _time.GetTimestamp();
                lock (_gate)
                {
                    if (_disposed)
                    {
                        start.Dispose();
                        return new WarmAcquireResult(WarmAcquireStatus.NoCapacity);
                    }

                    entry = new Entry
                    {
                        Key = key,
                        Sandbox = _factory.Create(key, limits),
                        Limits = limits,
                        CreatedTicks = now,
                        LastUsedTicks = now,
                        // A starting sandbox is never offered to anyone: busy until its caller is done.
                        Busy = true,
                        LentTicks = now,
                    };
                    if (!_entries.TryGetValue(key, out var list))
                    {
                        _entries[key] = list = [];
                    }
                    list.Add(entry);
                }
            }
            finally
            {
                lock (_gate)
                {
                    var left = _starting.GetValueOrDefault(key) - 1;
                    if (left <= 0) _starting.Remove(key); else _starting[key] = left;
                }
            }

            WarmStartResult started;
            try
            {
                started = await entry.Sandbox.StartAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                start.Dispose();
                // StartAsync turns host failures into results; this is the backstop for anything
                // that still escapes. Whatever it was, the entry, its memory and its container go.
                await DestroyAsync(entry, null).ConfigureAwait(false);
                if (token.IsCancellationRequested) throw;
                _logger.LogWarning("Starting warm sandbox {Name} failed: {Message}", entry.Sandbox.Name, ex.Message);
                return new WarmAcquireResult(WarmAcquireStatus.StartFailed, Start: new WarmStartResult
                {
                    Status = WarmStartStatus.HostFailure,
                    HostFailure = $"the warm sandbox could not be started: {ex.Message}",
                });
            }

            // Ready (or failed): the start is over, so is its place among the cold starts.
            start.Dispose();

            if (started.Status != WarmStartStatus.Ready)
            {
                await DestroyAsync(entry, null).ConfigureAwait(false);
                if (started.Status == WarmStartStatus.NoReuseSupport)
                {
                    lock (_gate) { _noReuse.Add(key.Image); }
                    _logger.LogInformation(
                        "Image {Image} has no reuse runtime (built on an older base image); its runs use a fresh sandbox each",
                        key.Image);
                    return new WarmAcquireResult(WarmAcquireStatus.NoReuseSupport, Start: started);
                }
                if (IsOwnLoadFailure(started)) Hold(key, started);
                return new WarmAcquireResult(WarmAcquireStatus.StartFailed, Start: started);
            }

            // Shut down (or drained) while it was starting: the entry is gone or going, and a
            // handle to it would be a sandbox no pool keeps. A drained one still serves this
            // caller — it was started for them — and is destroyed when the call ends.
            bool gone;
            lock (_gate) { gone = entry.Removed || _disposed; }
            if (gone)
            {
                await DestroyAsync(entry, "shutdown").ConfigureAwait(false);
                return new WarmAcquireResult(WarmAcquireStatus.NoCapacity);
            }

            // Started for a caller: its first call carries this start as its startup.
            return new WarmAcquireResult(
                WarmAcquireStatus.Acquired,
                new WarmHandle(entry, reused: false, startupMs: started.StartupMs),
                started);
        }

        /// <summary>
        /// Takes a sandbox back after its call. Returns why it was destroyed, or an empty string
        /// when it went back to the pool — exactly the <c>discard</c> field of the run's result.
        /// </summary>
        public async Task<string> ReleaseAsync(WarmHandle handle, WarmCallResult call)
        {
            ArgumentNullException.ThrowIfNull(handle);
            ArgumentNullException.ThrowIfNull(call);
            if (!handle.TryFinish()) return call.Discard ?? string.Empty;

            var entry = handle.Entry;
            var reason = call.Discard;

            bool draining;
            lock (_gate)
            {
                entry.Calls++;
                draining = entry.Draining || _disposed;
            }

            reason ??= entry.Sandbox.IsDead ? "crash" : null;
            reason ??= LimitReason(entry, call.MemoryBytes);
            reason ??= draining ? "drain" : null;

            if (reason is null)
            {
                // Paused before it is offered again: between calls nothing in it may run, so code an
                // earlier call left behind cannot act while nobody is watching.
                if (await entry.Sandbox.PauseAsync().ConfigureAwait(false))
                {
                    lock (_gate)
                    {
                        if (!entry.Removed && !_disposed && !entry.Draining)
                        {
                            entry.Busy = false;
                            entry.LastUsedTicks = _time.GetTimestamp();
                            return string.Empty;
                        }
                    }
                    reason = "drain";
                }
                else
                {
                    reason = "protocol";
                }
            }

            await DestroyAsync(entry, reason).ConfigureAwait(false);
            return reason;
        }

        /// <summary>Calls, age or memory — the limits that end a sandbox the runtime called clean.</summary>
        private string? LimitReason(Entry entry, long? memoryBytes)
        {
            if (entry.Calls >= _options.WarmMaxCalls) return "maxCalls";
            if (_time.GetElapsedTime(entry.CreatedTicks) >= TimeSpan.FromSeconds(_options.WarmMaxAgeSeconds)) return "maxAge";

            // Docker's working-set figure against the sandbox's own limit. Unknown memory is not a
            // reason: the cgroup still enforces the ceiling, and maxCalls/maxAge still recycle.
            if (memoryBytes is { } used && entry.Limits.MemoryBytes > 0 &&
                used * 100 >= entry.Limits.MemoryBytes * (long)_options.WarmMemoryHighWaterPercent)
            {
                return "memory";
            }

            return null;
        }

        /// <summary>Destroys a lent sandbox outright — the caller hit something it cannot vouch for.</summary>
        public Task DiscardAsync(WarmHandle handle, string reason)
        {
            ArgumentNullException.ThrowIfNull(handle);
            return handle.TryFinish() ? DestroyAsync(handle.Entry, reason) : Task.CompletedTask;
        }

        /// <summary>
        /// Gives back a sandbox the caller acquired but never sent a call to — the run turned out
        /// not to be runnable (an unreachable secret store, a refused envelope). It saw nothing of
        /// this run, so it goes back as it was: one from the pool was never resumed, and a new one
        /// is paused now.
        /// </summary>
        public async Task ReturnUnusedAsync(WarmHandle handle)
        {
            ArgumentNullException.ThrowIfNull(handle);
            if (!handle.TryFinish()) return;

            var entry = handle.Entry;
            if (!entry.Sandbox.IsDead && (handle.Paused || await entry.Sandbox.PauseAsync().ConfigureAwait(false)))
            {
                lock (_gate)
                {
                    if (!entry.Removed && !_disposed && !entry.Draining)
                    {
                        entry.Busy = false;
                        return;
                    }
                }
            }
            await DestroyAsync(entry, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Retires a version: idle sandboxes go now, busy (and starting) ones as soon as their
        /// call or start ends.
        /// </summary>
        /// <returns>How many were destroyed now.</returns>
        public async Task<int> DrainAsync(string tenantId, string functionId, string versionId)
        {
            List<Entry> idle = [];
            lock (_gate)
            {
                foreach (var (key, list) in _entries)
                {
                    if (key.TenantId != tenantId || key.FunctionId != functionId || key.VersionId != versionId) continue;
                    foreach (var entry in list)
                    {
                        entry.Draining = true;
                        if (!entry.Busy && !entry.Removed)
                        {
                            entry.Busy = true;
                            idle.Add(entry);
                        }
                    }
                }
            }

            foreach (var entry in idle) await DestroyAsync(entry, "drain").ConfigureAwait(false);
            return idle.Count;
        }

        /// <summary>
        /// The warm clean-up (RUNNER-FLOW step 6), on its own timer: per function, the most recently
        /// used idle sandbox is kept up to <see cref="RunnerOptions.WarmIdleSeconds"/> (600 s) idle and
        /// every other idle one up to <see cref="RunnerOptions.WarmExtraIdleSeconds"/> (60 s); older than
        /// <see cref="RunnerOptions.WarmMaxAgeSeconds"/> (1 h) or dead → removed. Busy ones are left alone.
        /// </summary>
        /// <returns>How many were destroyed.</returns>
        public async Task<int> SweepAsync()
        {
            List<(Entry Entry, string Reason)> expired = [];

            lock (_gate)
            {
                var byFunction = _entries.Values.SelectMany(e => e)
                    .Where(e => !e.Busy && !e.Removed)
                    .GroupBy(e => (e.Key.TenantId, e.Key.FunctionId));
                foreach (var function in byFunction)
                {
                    var kept = false;
                    foreach (var entry in function.OrderByDescending(e => e.LastUsedTicks))
                    {
                        var reason = ExpiryReason(entry, keepLonger: !kept);
                        if (reason is null)
                        {
                            kept = true;
                            continue;
                        }
                        entry.Busy = true;
                        expired.Add((entry, reason));
                    }
                }
            }

            foreach (var (entry, reason) in expired) await DestroyAsync(entry, reason).ConfigureAwait(false);
            return expired.Count;
        }

        /// <summary>Why an idle sandbox goes now, or null to keep it. Under the lock.</summary>
        private string? ExpiryReason(Entry entry, bool keepLonger)
        {
            if (entry.Sandbox.IsDead) return "crash";
            if (_time.GetElapsedTime(entry.CreatedTicks) >= TimeSpan.FromSeconds(_options.WarmMaxAgeSeconds)) return "maxAge";
            var idleLimit = TimeSpan.FromSeconds(keepLonger ? _options.WarmIdleSeconds : _options.WarmExtraIdleSeconds);
            return _time.GetElapsedTime(entry.LastUsedTicks) >= idleLimit ? "idle" : null;
        }

        /// <summary>
        /// Frees RAM by destroying the oldest idle sandbox (least recently used), of any key but
        /// <paramref name="except"/>. Used when a start — warm or single — or a build is refused for
        /// RAM (RUNNER-FLOW step 6.3): a paused sandbox nobody is calling is worth less than a run
        /// or a build that is waiting.
        /// </summary>
        /// <returns>False when there was no idle sandbox to give up.</returns>
        public async Task<bool> EvictIdleAsync(WarmKey? except = null)
        {
            Entry? victim;
            lock (_gate)
            {
                victim = _entries
                    .Where(kv => except is null || kv.Key != except)
                    .SelectMany(kv => kv.Value)
                    .Where(e => !e.Busy && !e.Removed)
                    .OrderBy(e => e.LastUsedTicks)
                    .FirstOrDefault();
                if (victim is not null) victim.Busy = true;
            }

            if (victim is null) return false;
            await DestroyAsync(victim, "evicted").ConfigureAwait(false);
            return true;
        }

        /// <summary>Removes the entry, and kills and removes its container.</summary>
        private async Task DestroyAsync(Entry entry, string? reason)
        {
            lock (_gate)
            {
                if (entry.Removed) return;
                entry.Removed = true;
                if (_entries.TryGetValue(entry.Key, out var list))
                {
                    list.Remove(entry);
                    if (list.Count == 0) _entries.Remove(entry.Key);
                }
            }

            if (reason is not null)
            {
                _logger.LogInformation(
                    "Destroying warm sandbox {Name} of function {FunctionId} after {Calls} call(s): {Reason}",
                    entry.Sandbox.Name, entry.Key.FunctionId, entry.Calls, reason);
            }

            await entry.Sandbox.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>Destroys every sandbox. Calls in flight finish on their own and are then destroyed.</summary>
        public async ValueTask DisposeAsync()
        {
            List<Entry> all;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                all = [.. _entries.Values.SelectMany(e => e)];
            }

            // Busy ones too: on shutdown nothing will be waiting on their answer for long, and a
            // container left behind would be the reaper's to find after the restart.
            await Task.WhenAll(all.Select(e => DestroyAsync(e, "shutdown"))).ConfigureAwait(false);
        }
    }
}
