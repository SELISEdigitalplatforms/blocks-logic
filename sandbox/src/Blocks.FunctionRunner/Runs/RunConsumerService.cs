using System.Globalization;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// The run loop: claim work, execute it, acknowledge it.
    /// <para>
    /// It refuses to claim anything while the host is unhealthy — most importantly while runsc
    /// is unavailable. Work left unclaimed stays in the stream and is picked up by a healthy
    /// runner, so an unhealthy host degrades the fleet's throughput and nothing else.
    /// </para>
    /// </summary>
    public sealed class RunConsumerService : BackgroundService
    {
        /// <summary>Most new entries read in one turn of the run loop (FN-4).</summary>
        internal const int MaxReadBatch = 16;

        private readonly IDatabase _db;
        private readonly RunProcessor _processor;
        private readonly HeartbeatService _heartbeat;
        private readonly HostBudget _budget;
        private readonly RunnerOptions _options;
        private readonly ILogger<RunConsumerService> _logger;

        public RunConsumerService(
            IDatabase db,
            RunProcessor processor,
            HeartbeatService heartbeat,
            HostBudget budget,
            IOptions<RunnerOptions> options,
            ILogger<RunConsumerService> logger)
        {
            _db = db;
            _processor = processor;
            _heartbeat = heartbeat;
            _budget = budget;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.ProcessRuns)
            {
                _logger.LogInformation("Run processing is disabled on this host");
                return;
            }

            var consumer = new GroupConsumer(
                _db, _logger, RedisKeys.RunsStream, RedisKeys.RunnerGroup, _options.RunnerId);
            // Function-event runs: same group name, entry format and handling, their own stream (see RunSourceSelector).
            var eventConsumer = new GroupConsumer(
                _db, _logger, RedisKeys.EventRunsStream, RedisKeys.RunnerGroup, _options.RunnerId);
            await consumer.EnsureGroupAsync().ConfigureAwait(false);
            await eventConsumer.EnsureGroupAsync().ConfigureAwait(false);
            var selector = new RunSourceSelector();

            // The capacity logged here is the budget's current view, not a configured number:
            // it moves with the host while this loop runs.
            _logger.LogInformation(
                "Consuming {Stream} and {EventStream} as {Consumer} (capacity {Capacity}); function run spans: {Tracing}",
                RedisKeys.RunsStream, RedisKeys.EventRunsStream, _options.RunnerId, _budget.Capacity, _processor.TracingState);

            var idleDelay = TimeSpan.FromMilliseconds(250);
            // Wakes the idle wait below the moment the control plane queues a run.
            using var wakeup = await StreamWakeup.SubscribeAsync(_db, RedisKeys.RunsNudgeChannel).ConfigureAwait(false);
            using var eventWakeup = await StreamWakeup.SubscribeAsync(_db, RedisKeys.EventRunsNudgeChannel).ConfigureAwait(false);
            var reclaimEvery = TimeSpan.FromSeconds(30);
            var nextReclaim = DateTimeOffset.UtcNow.Add(reclaimEvery);

            // Runs in progress on this host. Each claimed entry is handled on its own task, so a
            // run that takes 20 s no longer holds up every run queued behind it: the loop used to
            // await each run before claiming the next, so a host of capacity 10 ran one run at a
            // time. Only this loop touches the list.
            var running = new List<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    running.RemoveAll(static t => t.IsCompleted);

                    // Never claim work this host cannot safely execute.
                    var readiness = _heartbeat.Latest;
                    if (readiness is { Healthy: false })
                    {
                        _logger.LogWarning("Not claiming work — the host is unhealthy: {Summary}", readiness.Summary);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    // Claim only what this host has room to work on. An entry claimed beyond that
                    // would only be deferred, and a deferred entry waits for the reclaim sweep —
                    // left on the stream, another runner with room takes it at once.
                    var room = ParallelLimit() - running.Count;
                    if (room <= 0)
                    {
                        await Task.WhenAny([.. running, Task.Delay(idleDelay, stoppingToken)]).ConfigureAwait(false);
                        continue;
                    }

                    // Up to the room this host has, at most MaxReadBatch: one round trip per entry
                    // capped a runner at ~1/RTT claims per second (~29/s at 34 ms, FN-4). Never more
                    // than the room, so nothing is claimed only to be deferred.
                    var entries = await ReadNextAsync(
                        consumer, eventConsumer, selector, Math.Min(room, MaxReadBatch), stoppingToken,
                        _options.EventRunMaxWaitSeconds).ConfigureAwait(false);

                    // On its own timer: the sweep for entries a dead runner left behind used to run
                    // only when no new entry arrived, so under steady traffic it never ran.
                    if (DateTimeOffset.UtcNow >= nextReclaim && room > entries.Count)
                    {
                        nextReclaim = DateTimeOffset.UtcNow.Add(reclaimEvery);
                        entries = await ReclaimAsync(consumer, eventConsumer, entries, room).ConfigureAwait(false);
                    }

                    if (entries.Count == 0)
                    {
                        await WaitEitherAsync(wakeup, eventWakeup, idleDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var (source, entry) in entries)
                    {
                        // Not the stop token: a stopping host stops claiming but lets what it has
                        // started finish (each run has its own deadline, ≤ startup allowance +
                        // timeout + grace). Cancelling here killed calls half-way and ran them
                        // again elsewhere ≥ 90 s later — side effects twice (FN-3, 2026-10-06).
                        running.Add(HandleGuardedAsync(source, entry, CancellationToken.None));
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A failure here must never stop the loop; the run is retried by whoever
                    // claims it next.
                    _logger.LogError(ex, "The run loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }

            // A stopping host finishes what it started (each run sees the same token) before it
            // reports the loop stopped; what it never claimed stays on the stream for others.
            try
            {
                await Task.WhenAll(running).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Each task logs its own failure.
            }

            _logger.LogInformation("Run loop stopped");
        }

        /// <summary>The sweep for entries a dead runner left behind, on both run streams, within the room left.</summary>
        private async Task<List<(GroupConsumer Source, ClaimedEntry Entry)>> ReclaimAsync(
            GroupConsumer runs, GroupConsumer events, List<(GroupConsumer Source, ClaimedEntry Entry)> entries, int room)
        {
            var minIdle = TimeSpan.FromMilliseconds(_options.ClaimIdleMs);
            var reclaimed = await runs.ReclaimAbandonedAsync(minIdle, count: Math.Min(5, room - entries.Count)).ConfigureAwait(false);
            entries = [.. entries, .. reclaimed.Select(e => (runs, e))];
            if (room <= entries.Count) return entries;

            var reclaimedEvents = await events.ReclaimAbandonedAsync(minIdle, count: Math.Min(5, room - entries.Count)).ConfigureAwait(false);
            return [.. entries, .. reclaimedEvents.Select(e => (events, e))];
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> new entries from the two run streams by the 1:5 rule
        /// (<see cref="RunSourceSelector"/>): runs first, one event run after every five while both wait (every
        /// two while the event queue is behind), and every free slot to events when the runs stream is empty.
        /// An expired event entry is still returned (its handler fails it as EVENT_EXPIRED without a sandbox),
        /// but it does not use the event turn: the next event entry is read at once, within the room.
        /// </summary>
        internal static async Task<List<(GroupConsumer Source, ClaimedEntry Entry)>> ReadNextAsync(
            GroupConsumer runs, GroupConsumer events, RunSourceSelector selector, int count, CancellationToken token,
            int eventMaxWaitSeconds = RunnerOptions.DefaultEventRunMaxWaitSeconds)
        {
            var taken = new List<(GroupConsumer, ClaimedEntry)>();
            var want = count;

            while (want > 0 && selector.EventDue(DateTimeOffset.UtcNow))
            {
                var due = await events.ReadNewAsync(count: 1, token).ConfigureAwait(false);
                taken.AddRange(due.Select(e => (events, e)));
                want -= due.Count;
                NoteEventsRead(selector, due, eventMaxWaitSeconds);
            }

            var runsWanted = selector.RunsToRead(want, DateTimeOffset.UtcNow);
            var fromRuns = runsWanted > 0
                ? await runs.ReadNewAsync(runsWanted, token).ConfigureAwait(false)
                : [];
            taken.AddRange(fromRuns.Select(e => (runs, e)));
            selector.TookRuns(fromRuns.Count);
            want -= fromRuns.Count;

            // The runs stream had less than asked for: the free slots go to events.
            if (want > 0 && fromRuns.Count < runsWanted)
            {
                var fill = await events.ReadNewAsync(want, token).ConfigureAwait(false);
                taken.AddRange(fill.Select(e => (events, e)));
                NoteEventsRead(selector, fill, eventMaxWaitSeconds);
            }
            return taken;
        }

        private static void NoteEventsRead(RunSourceSelector selector, IReadOnlyCollection<ClaimedEntry> read, int maxWaitSeconds)
        {
            if (read.Count == 0)
            {
                selector.EventsEmpty(DateTimeOffset.UtcNow);
                return;
            }

            var live = 0;
            long longest = 0;
            foreach (var entry in read)
            {
                var waited = EntryWaitedMs(entry);
                if (waited is { } ms) longest = Math.Max(longest, ms);
                if (!IsEventExpired(waited, maxWaitSeconds)) live++;
            }
            selector.TookEvents(live);
            selector.EventWaited(longest, maxWaitSeconds * 1000L);
        }

        /// <summary>
        /// Waits for the first nudge of either stream, or the interval. The loser's wait is cancelled so it
        /// cannot swallow a later nudge.
        /// </summary>
        private static async Task WaitEitherAsync(StreamWakeup first, StreamWakeup second, TimeSpan interval, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var a = first.WaitAsync(interval, cts.Token);
            var b = second.WaitAsync(interval, cts.Token);
            await Task.WhenAny(a, b).ConfigureAwait(false);
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(a, b).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // The wait that lost the race.
            }
        }

        /// <summary>True when an event run waited longer than <paramref name="maxWaitSeconds"/> in its queue.</summary>
        internal static bool IsEventExpired(long? queuedMs, int maxWaitSeconds) =>
            queuedMs is { } waited && waited > maxWaitSeconds * 1000L;

        /// <summary>
        /// A run that cannot start yet goes back to the end of the queue after a short back-off
        /// (100 ms doubling to 2 s), and this delivery is acknowledged. Deferrals are counted apart
        /// from crash re-deliveries (which keep MaxAttempts) and given a time budget from when the
        /// run was first queued; past it, the run is dead-lettered with the reason, which closes it
        /// as undeliverable. The back-off is spent inside this run's slot of the parallel loop, so
        /// a host full of waiting runs claims fewer new ones on its own.
        /// <para>
        /// Put back before acknowledging: a crash in between leaves two entries for one run, and the
        /// lease plus the "already finished" check make the second a no-op — never a lost run.
        /// </para>
        /// </summary>
        private async Task RequeueDeferredAsync(GroupConsumer consumer, ClaimedEntry entry)
        {
            var deferrals = int.TryParse(entry.Get(RedisKeys.RunDeferralsField), NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                ? d : 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var firstQueued = long.TryParse(entry.Get(RedisKeys.RunFirstQueuedField), NumberStyles.None, CultureInfo.InvariantCulture, out var f)
                ? f
                : now - (StreamEntryAgeMs(entry.Id) ?? 0);

            if (now - firstQueued > _options.DeferralBudgetSeconds * 1000L)
            {
                await consumer.DeadLetterAsync(entry,
                    $"no capacity to start it within {_options.DeferralBudgetSeconds} s ({deferrals} deferrals)").ConfigureAwait(false);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2000, 100 << Math.Min(deferrals, 5)))).ConfigureAwait(false);

            var fields = entry.Fields
                .Where(kv => kv.Key is not (RedisKeys.RunDeferralsField or RedisKeys.RunFirstQueuedField))
                .Select(kv => new NameValueEntry(kv.Key, kv.Value))
                .Append(new NameValueEntry(RedisKeys.RunDeferralsField, (deferrals + 1).ToString(CultureInfo.InvariantCulture)))
                .Append(new NameValueEntry(RedisKeys.RunFirstQueuedField, firstQueued.ToString(CultureInfo.InvariantCulture)))
                .ToArray();
            await _db.StreamAddAsync(consumer.Stream, fields).ConfigureAwait(false);
            await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
        }

        /// <summary>
        /// How long an entry has waited: from when its run was first queued (kept across deferrals), else from
        /// the entry id; null if neither is readable.
        /// </summary>
        internal static long? EntryWaitedMs(ClaimedEntry entry) =>
            long.TryParse(entry.Get(RedisKeys.RunFirstQueuedField), NumberStyles.None, CultureInfo.InvariantCulture, out var first)
                ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - first)
                : StreamEntryAgeMs(entry.Id);

        /// <summary>Milliseconds since a stream entry was added, from its id (<c>ms-seq</c>); null if unreadable.</summary>
        internal static long? StreamEntryAgeMs(RedisValue id)
        {
            var text = id.ToString();
            var dash = text.IndexOf('-', StringComparison.Ordinal);
            return long.TryParse(dash > 0 ? text[..dash] : text, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms)
                : null;
        }

        /// <summary>The cap on runs in progress: the configured one, else the host's current capacity.</summary>
        private int ParallelLimit() =>
            _options.MaxParallelRuns > 0 ? _options.MaxParallelRuns : Math.Max(1, _budget.Capacity);

        /// <summary>
        /// One claimed entry, handled off the loop. Whatever goes wrong stays with this run: it is
        /// logged, the entry is left unacknowledged, and whoever claims it next retries it — the
        /// loop and every other run go on.
        /// </summary>
        private Task HandleGuardedAsync(GroupConsumer consumer, ClaimedEntry entry, CancellationToken token) =>
            Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(consumer, entry, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Shutting down; the entry stays pending for another runner.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Run entry {EntryId} hit an unexpected error", entry.Id);
                }
            }, CancellationToken.None);

        private async Task HandleAsync(GroupConsumer consumer, ClaimedEntry entry, CancellationToken token)
        {
            var runId = entry.Get("runId");
            if (string.IsNullOrWhiteSpace(runId))
            {
                await consumer.DeadLetterAsync(entry, "the entry carries no runId").ConfigureAwait(false);
                return;
            }

            if (ReadProtocol(entry) is not { } protocol)
            {
                await consumer.DeadLetterAsync(entry,
                    $"protocol version '{entry.Get("protocol")}' is not supported by this runner " +
                    $"(expected {RedisKeys.MinRunProtocolVersion}-{RedisKeys.RunProtocolVersion})").ConfigureAwait(false);
                return;
            }

            // A job that keeps killing whoever picks it up is retired rather than left to block
            // the stream for everyone else.
            var deliveries = await consumer.DeliveryCountAsync(entry).ConfigureAwait(false);
            if (deliveries > _options.MaxAttempts)
            {
                await consumer.DeadLetterAsync(entry,
                    $"delivered {deliveries} times without completing").ConfigureAwait(false);
                return;
            }

            var job = new RunJob
            {
                RunId = runId,
                FunctionId = entry.Get("functionId") ?? runId,
                VersionId = entry.Get("versionId"),
                TenantId = entry.Get("tenantId"),
                Image = entry.Get("image") ?? string.Empty,
                ArtifactUrl = entry.Get(RedisKeys.RunArtifactUrlField),
                ArtifactSha256 = entry.Get(RedisKeys.RunArtifactSha256Field),
                Attempt = ReadAttempt(entry),
                Deliveries = deliveries,
                Protocol = protocol,
                // The function's opt-in only; whether this host does reuse at all is RunProcessor's
                // question (RunnerOptions.SandboxReuse).
                Reuse = string.Equals(entry.Get(RedisKeys.RunReuseField), "1", StringComparison.Ordinal),
                ApiTimings = entry.Get(RedisKeys.RunApiTimingsField),
                // From when the run was first queued, across deferrals.
                QueuedMs = EntryWaitedMs(entry),
                TraceParent = entry.Get(RedisKeys.TraceParentField),
            };

            if (deliveries > 1)
            {
                // The same attempt handed out again — a runner died or stalled holding it. Not a
                // new attempt: the attempt number is the control plane's, and it keys output-action
                // idempotency, so a redelivery must report the attempt it is redelivering.
                _logger.LogWarning(
                    "Run {RunId} attempt {Attempt} is being redelivered (delivery {Deliveries})",
                    runId, job.Attempt, deliveries);
            }

            if (string.IsNullOrWhiteSpace(job.Image))
            {
                await consumer.DeadLetterAsync(entry, "the entry carries no image").ConfigureAwait(false);
                return;
            }

            // A run started by an event is not real time: past its wait limit it is not started. The run is
            // failed through the same result path as any other run that never reached a sandbox, and the
            // control plane closes it as terminal (never retried) and dead-letters the event.
            if (string.Equals(consumer.Stream, RedisKeys.EventRunsStream, StringComparison.Ordinal)
                && IsEventExpired(job.QueuedMs, _options.EventRunMaxWaitSeconds))
            {
                await _processor.ReportUnexecutedAsync(
                    job, RunStatuses.Failed, ErrorCodes.EventExpired,
                    $"{ErrorCodes.EventExpired}: the event run waited more than {_options.EventRunMaxWaitSeconds} s in the queue and was not started",
                    token).ConfigureAwait(false);
                await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
                _logger.LogWarning(
                    "Event run {RunId} expired after {WaitedMs} ms in the queue; not started", runId, job.QueuedMs);
                return;
            }

            var disposition = await GuardAsync(() => _processor.ProcessAsync(job, token), runId, _logger, token)
                .ConfigureAwait(false);

            switch (disposition)
            {
                case RunProcessor.Disposition.Complete:
                    // Acknowledged last, and only now: the result is already durable.
                    await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
                    break;

                case RunProcessor.Disposition.Deferred:
                    await RequeueDeferredAsync(consumer, entry).ConfigureAwait(false);
                    break;

                case RunProcessor.Disposition.NotOurs:
                    // Another runner owns it; do not acknowledge someone else's work.
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Runs one entry so that only a real shutdown can stop the loop. A cancellation nobody
        /// asked this loop for — a Docker call timing out inside HttpClient, a run's lease token —
        /// used to escape into the loop's shutdown handler and end the run loop for good, with the
        /// process still looking healthy. Such a run is left pending: the stream hands it out again.
        /// </summary>
        internal static async Task<RunProcessor.Disposition> GuardAsync(
            Func<Task<RunProcessor.Disposition>> process, string runId, ILogger logger, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(process);
            try
            {
                return await process().ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Run {RunId} was interrupted by a cancellation that was not a shutdown ({Message}); leaving it pending",
                    runId, ex.Message);
                return RunProcessor.Disposition.Deferred;
            }
        }

        /// <summary>
        /// The run entry's protocol version, or null when this runner must not execute it. An
        /// entry without the field predates versioning and is version 1. Anything outside the
        /// supported window — or unparseable — is refused whole: executing an envelope whose
        /// meaning this runner does not know is how a function ends up holding the wrong thing in
        /// <c>ctx.env</c> (see <see cref="RedisKeys.RunProtocolVersion"/>).
        /// </summary>
        internal static int? ReadProtocol(ClaimedEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var raw = entry.Get("protocol");
            if (string.IsNullOrEmpty(raw)) return RedisKeys.MinRunProtocolVersion;
            return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var protocol)
                && protocol >= RedisKeys.MinRunProtocolVersion
                && protocol <= RedisKeys.RunProtocolVersion
                ? protocol
                : null;
        }

        /// <summary>
        /// The attempt number the control plane assigned to this entry (1 for the first run, N
        /// for its Nth retry). It is the control plane's number, not the stream's delivery count:
        /// a retry is a new entry whose own delivery count starts at 1 again, and a redelivered
        /// entry is the same attempt however many times it is handed out. Anything absent,
        /// unparseable or below 1 reads as 1, the value a producer that predates the field meant.
        /// </summary>
        internal static int ReadAttempt(ClaimedEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            return int.TryParse(entry.Get("attempt"), NumberStyles.None, CultureInfo.InvariantCulture, out var attempt)
                && attempt >= 1
                ? attempt
                : 1;
        }
    }
}
