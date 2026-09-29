using Blocks.Genesis;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Consumes <c>functions:dead</c> and closes the record of work that was never executed.
    /// <para>
    /// The runner dead-letters an entry it cannot process — no <c>runId</c>, no image, an
    /// unsupported protocol version, or a delivery count past its budget — and acknowledges it, so
    /// nothing will ever run it. Until this consumer existed nothing on this side was listening:
    /// the Mongo record stayed <c>QUEUED</c> for ever, a caller polling <c>GetRun</c> was told
    /// QUEUED for ever, and the only trace was a stream nobody read. That is the failure mode this
    /// closes — the run is reported FAILED / <see cref="RunErrorCode.Undeliverable"/>, and the
    /// build is reported FAILED, with the runner's own reason attached.
    /// </para>
    /// <para>
    /// Three things make it safe to run alongside everything else:
    /// <list type="bullet">
    /// <item>the write is conditional on the record still being non-terminal, so a dead letter
    /// that arrives after a real result — possible, because a runner can publish and then die
    /// before acknowledging — changes nothing;</item>
    /// <item>entries are acknowledged but <b>not</b> deleted, because the dead stream is the
    /// forensic record of lost work. <see cref="FunctionStreamTrimmer"/> ages it out instead;</item>
    /// <item>because they are not deleted, a re-delivery can present the same entry twice, so an
    /// applied entry leaves a short-lived marker in Redis and the second pass is a no-op.</item>
    /// </list>
    /// </para>
    /// <para>
    /// No retry is ever scheduled from here. A dead letter means the delivery budget is already
    /// spent; re-enqueueing would put the same poisonous job back on the same stream on a timer.
    /// A developer can replay the run explicitly, which is a decision rather than a loop.
    /// </para>
    /// </summary>
    public sealed class FunctionDeadLetterConsumer : BackgroundService
    {
        private const int MaxConcurrentEntries = 5;
        private const int MaxDeliveryAttempts = 5;
        private static readonly TimeSpan ReclaimIdle = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ReclaimInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

        private readonly IDatabase _db;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionBuildRepository _buildRepository;
        private readonly ILogger<FunctionDeadLetterConsumer> _logger;

        public FunctionDeadLetterConsumer(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionBuildRepository buildRepository,
            ILogger<FunctionDeadLetterConsumer> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _buildRepository = buildRepository;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";
            var consumer = new FunctionResultGroupConsumer(
                _db, _logger, FunctionQueueKeys.DeadStream, consumerName, deleteOnAcknowledge: false);
            await consumer.EnsureGroupAsync();

            var nextReclaim = DateTimeOffset.UtcNow;

            _logger.LogInformation("Consuming {Stream} as {Consumer}", FunctionQueueKeys.DeadStream, consumerName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var entries = await consumer.ReadNewAsync(count: MaxConcurrentEntries);

                    if (entries.Count == 0 && DateTimeOffset.UtcNow >= nextReclaim)
                    {
                        nextReclaim = DateTimeOffset.UtcNow.Add(ReclaimInterval);
                        entries = await consumer.ReclaimAbandonedAsync(ReclaimIdle, count: MaxConcurrentEntries);
                    }

                    if (entries.Count == 0)
                    {
                        // Slower than the result loops on purpose: a dead letter is a rare event
                        // and nobody is waiting on this poll the way a sync caller waits on a
                        // result. Nothing here is urgent enough to spin at 250 ms.
                        await Task.Delay(IdleDelay, stoppingToken);
                        continue;
                    }

                    // Sequential, unlike the result consumers. There is no slow external call in
                    // this path — one conditional Mongo update — and volume is low by definition,
                    // so the concurrency those need buys nothing here.
                    foreach (var entry in entries)
                    {
                        await HandleAsync(consumer, entry, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The Functions dead-letter loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        private async Task HandleAsync(
            FunctionResultGroupConsumer consumer, ResultStreamEntry entry, CancellationToken stoppingToken)
        {
            try
            {
                await ProcessAsync(entry, stoppingToken);
                await consumer.AcknowledgeAsync(entry.Id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. Leave the entry pending: it is reclaimed and applied by whoever
                // picks it up next, which is the whole point of acknowledging last.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply dead-letter entry {Id}", entry.Id);

                // This is the last stop. An entry that cannot be applied here has nowhere further
                // to go — moving it to functions:dead-results would only hide it — so once the
                // budget is spent it is acknowledged with a loud log and left in the stream,
                // where the trimmer's retention still keeps it readable for a week.
                var deliveries = await consumer.DeliveryCountAsync(entry.Id);
                if (deliveries >= MaxDeliveryAttempts)
                {
                    _logger.LogCritical(
                        "Giving up on dead-letter entry {Id} after {Deliveries} attempts; the run or build it " +
                        "names keeps its current status and must be closed by hand. Fields: {Fields}",
                        entry.Id, deliveries, string.Join(", ", entry.Fields.Select(f => $"{f.Key}={f.Value}")));
                    await consumer.AcknowledgeAsync(entry.Id);
                }
            }
        }

        /// <summary>
        /// Applies one dead-lettered entry. Internal so the tests can drive a single entry through
        /// it without standing up the loop.
        /// </summary>
        internal async Task ProcessAsync(ResultStreamEntry entry, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var reason = entry.Get("deadReason");
            var sourceStream = entry.Get("sourceStream");
            var tenantId = entry.Get("tenantId");

            // Database-per-tenant: without a tenant there is no collection to write to. This is
            // reachable — the runner dead-letters an entry carrying no runId at all, and such an
            // entry may well carry no tenantId either — so it is logged rather than thrown, and
            // acknowledged, because retrying cannot make a tenant appear.
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogError(
                    "Dead-lettered entry {Id} from {Source} carries no tenantId, so the record it names cannot " +
                    "be located. Reason: {Reason}", entry.Id, sourceStream, reason);
                return;
            }

            var applied = FunctionQueueKeys.DeadApplied(entry.Id.ToString());
            if (await _db.KeyExistsAsync(applied))
            {
                _logger.LogDebug("Dead-letter entry {Id} was already applied; skipping", entry.Id);
                return;
            }

            var handled = sourceStream switch
            {
                FunctionQueueKeys.RunsStream => await ApplyRunAsync(tenantId, entry, reason, cancellationToken),
                FunctionQueueKeys.BuildsStream => await ApplyBuildAsync(tenantId, entry, reason, cancellationToken),
                _ => await ApplyByShapeAsync(tenantId, entry, reason, sourceStream, cancellationToken),
            };

            if (handled)
            {
                // Set after the write, never before: a marker written first would suppress the
                // retry of an update that then failed.
                // The overload is spelled out rather than left to defaults: StringSetAsync has
                // several, and which one a bare three-argument call binds to changes with the
                // client version.
                await _db.StringSetAsync(
                    applied, DateTimeOffset.UtcNow.ToString("O"), FunctionQueueKeys.DeadAppliedTtl,
                    When.Always, CommandFlags.None);
            }
        }

        /// <summary>
        /// A dead letter whose <c>sourceStream</c> is missing or unrecognised — an older runner,
        /// or a field that did not survive. The ids are unambiguous, so fall back to them rather
        /// than dropping a record that can plainly be closed.
        /// </summary>
        private async Task<bool> ApplyByShapeAsync(
            string tenantId, ResultStreamEntry entry, string? reason, string? sourceStream, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(entry.Get("runId")))
            {
                _logger.LogWarning(
                    "Dead-lettered entry {Id} has sourceStream '{Source}'; treating it as a run because it carries a runId",
                    entry.Id, sourceStream);
                return await ApplyRunAsync(tenantId, entry, reason, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(entry.Get("buildId")))
            {
                _logger.LogWarning(
                    "Dead-lettered entry {Id} has sourceStream '{Source}'; treating it as a build because it carries a buildId",
                    entry.Id, sourceStream);
                return await ApplyBuildAsync(tenantId, entry, reason, cancellationToken);
            }

            _logger.LogError(
                "Dead-lettered entry {Id} from '{Source}' names neither a run nor a build; nothing to close. Reason: {Reason}",
                entry.Id, sourceStream, reason);
            return false;
        }

        private async Task<bool> ApplyRunAsync(
            string tenantId, ResultStreamEntry entry, string? reason, CancellationToken cancellationToken)
        {
            var runId = entry.Get("runId");
            if (string.IsNullOrWhiteSpace(runId))
            {
                _logger.LogError(
                    "Dead-lettered run entry {Id} carries no runId. Reason: {Reason}", entry.Id, reason);
                return false;
            }

            var message = Describe("the run never executed", reason);
            var closed = await _runRepository.FailIfNotTerminalAsync(
                tenantId, runId, RunErrorCode.Undeliverable, message, DateTime.UtcNow, cancellationToken);

            if (closed)
            {
                _logger.LogError(
                    "Run {RunId} was dead-lettered and has been closed as FAILED/{Code}: {Reason}",
                    runId, nameof(RunErrorCode.Undeliverable), reason);

                // Wake anyone still waiting synchronously. Without this a caller inside its wait
                // window sits out the full timeout for a run that is already decided.
                await _db.PublishAsync(
                    RedisChannel.Literal(FunctionQueueKeys.SyncChannel(runId)), FunctionQueueKeys.Wire.Failed);
            }
            else
            {
                // Either the run already reached a terminal status — the benign race — or it does
                // not exist, which for a run whose record has passed its retention is expected.
                _logger.LogWarning(
                    "Run {RunId} was dead-lettered but is already closed or no longer exists; leaving it as it is. Reason: {Reason}",
                    runId, reason);
            }

            return true;
        }

        private async Task<bool> ApplyBuildAsync(
            string tenantId, ResultStreamEntry entry, string? reason, CancellationToken cancellationToken)
        {
            var buildId = entry.Get("buildId");
            if (string.IsNullOrWhiteSpace(buildId))
            {
                _logger.LogError(
                    "Dead-lettered build entry {Id} carries no buildId. Reason: {Reason}", entry.Id, reason);
                return false;
            }

            var message = Describe("the build never ran", reason);
            var closed = await _buildRepository.FailIfNotTerminalAsync(
                tenantId, buildId, message, DateTime.UtcNow, cancellationToken);

            if (closed)
            {
                _logger.LogError("Build {BuildId} was dead-lettered and has been closed as FAILED: {Reason}", buildId, reason);
            }
            else
            {
                _logger.LogWarning(
                    "Build {BuildId} was dead-lettered but is already closed or no longer exists; leaving it as it is. Reason: {Reason}",
                    buildId, reason);
            }

            return true;
        }

        /// <summary>
        /// The message a developer reads. The runner's reason is appended rather than replaced:
        /// on its own "delivered 4 times without completing" does not say that nothing ran, which
        /// is the part that decides whether replaying is safe.
        /// </summary>
        internal static string Describe(string prefix, string? reason) =>
            string.IsNullOrWhiteSpace(reason) ? $"{prefix}." : $"{prefix}: {reason}";
    }
}
