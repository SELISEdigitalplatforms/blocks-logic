using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Delivers output actions, off <c>functions:outputs</c> — split out of
    /// <see cref="FunctionResultConsumer"/> so that a slow endpoint (up to the function's whole
    /// retry policy, minutes) never holds a result entry long enough to be reclaimed by another
    /// Worker and delivered twice.
    /// <para>
    /// <b>At most once per action, per run attempt.</b> Before an action is sent, its marker
    /// <see cref="FunctionWorkerQueueKeys.OutputActionMarker"/> — keyed on run, attempt and
    /// action — is taken with SET NX. Only the Worker that took it sends the action; it keeps the
    /// marker (and its stream entry) alive with a heartbeat while it works, then records the
    /// outcome on the marker. A redelivered or reclaimed job that finds a recorded outcome
    /// reuses it; one that finds a live marker leaves the job to its owner; one that finds a
    /// marker whose owner stopped heart-beating mid-send does <b>not</b> send again — whether the
    /// endpoint received it is unknowable, so the action is reported failed with that reason and a
    /// developer can replay deliberately. A duplicate side effect is the one outcome this avoids
    /// at any price.
    /// </para>
    /// <para>
    /// Chain semantics are <see cref="OutputActionProcessor"/>'s own — in order, stop at the first
    /// failure — kept by handing it one action at a time, which is what lets each one carry its
    /// own marker. The verdict is written only while the run is still OUTPUT_PROCESSING for that
    /// attempt, so a duplicate job cannot rewrite it.
    /// </para>
    /// <para>
    /// <b>Fairness.</b> A global cap (<c>Functions:OutputActionConcurrency</c>, default
    /// <see cref="DefaultConcurrency"/>) and a per-tenant cap
    /// (<c>Functions:OutputActionPerTenantConcurrency</c>, default
    /// <see cref="DefaultPerTenantConcurrency"/>). A job whose tenant is at its cap goes back to
    /// the tail of the stream, so one tenant with a thousand slow webhooks cannot take every slot.
    /// </para>
    /// </summary>
    public sealed class FunctionOutputActionConsumer : BackgroundService
    {
        internal const int DefaultConcurrency = 16;
        internal const int DefaultPerTenantConcurrency = 4;
        private const int MaxDeliveryAttempts = 5;

        private static readonly TimeSpan ReclaimIdle = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ReclaimInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// How often a Worker delivering an action proves it is still alive — on the marker, and
        /// by re-claiming its stream entry so the entry's idle time never reaches
        /// <see cref="ReclaimIdle"/> while the work is genuinely in progress.
        /// </summary>
        internal TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>A marker whose heartbeat is older than this belongs to a Worker that stopped.</summary>
        internal TimeSpan MarkerStaleAfter { get; set; } = ReclaimIdle;

        private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web);

        private readonly IDatabase _db;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionRepository _functionRepository;
        private readonly IOutputActionProcessor _processor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionOutputActionConsumer> _logger;
        private readonly string _consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";

        public FunctionOutputActionConsumer(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionRepository functionRepository,
            IOutputActionProcessor processor,
            IConfiguration configuration,
            ILogger<FunctionOutputActionConsumer> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _versionRepository = versionRepository;
            _functionRepository = functionRepository;
            _processor = processor;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>What processing one job decided.</summary>
        internal enum OutputDisposition
        {
            /// <summary>Finished, or nothing to do. Acknowledge.</summary>
            Done,

            /// <summary>Another live Worker is delivering one of its actions. Leave the entry alone.</summary>
            InFlightElsewhere,
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var concurrency = Math.Clamp(
                _configuration.GetValue("Functions:OutputActionConcurrency", DefaultConcurrency), 1, 256);
            var perTenant = Math.Clamp(
                _configuration.GetValue("Functions:OutputActionPerTenantConcurrency", DefaultPerTenantConcurrency),
                1, concurrency);

            var consumer = new FunctionResultGroupConsumer(
                _db, _logger, FunctionWorkerQueueKeys.OutputsStream, _consumerName);
            await consumer.EnsureGroupAsync();

            using var gate = new SemaphoreSlim(concurrency);
            var inFlightByTenant = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            var nextReclaim = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Consuming {Stream} as {Consumer} ({Concurrency} at once, {PerTenant} per tenant)",
                FunctionWorkerQueueKeys.OutputsStream, _consumerName, concurrency, perTenant);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Never read more than there are free slots for: an entry read is an entry
                    // owned, and one owned but not started only ages toward a reclaim.
                    await gate.WaitAsync(stoppingToken);
                    var free = gate.CurrentCount + 1;
                    gate.Release();

                    var entries = await consumer.ReadNewAsync(count: free);
                    if (entries.Count == 0 && DateTimeOffset.UtcNow >= nextReclaim)
                    {
                        nextReclaim = DateTimeOffset.UtcNow.Add(ReclaimInterval);
                        entries = await consumer.ReclaimAbandonedAsync(ReclaimIdle, count: free);
                    }

                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                        continue;
                    }

                    var requeued = 0;
                    foreach (var entry in entries)
                    {
                        var tenantId = entry.Get("tenantId") ?? string.Empty;
                        if (inFlightByTenant.GetValueOrDefault(tenantId) >= perTenant)
                        {
                            await RequeueAsync(consumer, entry);
                            requeued++;
                            continue;
                        }

                        await gate.WaitAsync(stoppingToken);
                        inFlightByTenant.AddOrUpdate(tenantId, 1, (_, n) => n + 1);
                        _ = HandleAsync(consumer, entry, gate, inFlightByTenant, tenantId, stoppingToken);
                    }

                    if (requeued == entries.Count)
                    {
                        // Everything read belonged to tenants already at their cap; do not spin
                        // re-reading the same entries.
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The Functions output-action loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        /// <summary>Moves a job to the tail of the stream: added again first, then acknowledged.</summary>
        private async Task RequeueAsync(FunctionResultGroupConsumer consumer, ResultStreamEntry entry)
        {
            var fields = entry.Fields
                .Where(f => f.Key != "requeues")
                .Select(f => new NameValueEntry(f.Key, f.Value))
                .Append(new NameValueEntry("requeues", entry.GetInt("requeues") + 1))
                .ToArray();
            await _db.StreamAddAsync(FunctionWorkerQueueKeys.OutputsStream, fields);
            await consumer.AcknowledgeAsync(entry.Id);
        }

        private async Task HandleAsync(
            FunctionResultGroupConsumer consumer, ResultStreamEntry entry, SemaphoreSlim gate,
            ConcurrentDictionary<string, int> inFlightByTenant, string tenantId, CancellationToken stoppingToken)
        {
            try
            {
                var disposition = await ProcessAsync(
                    entry, () => KeepEntryAliveAsync(entry.Id), stoppingToken);
                if (disposition == OutputDisposition.Done)
                {
                    await consumer.AcknowledgeAsync(entry.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process output job {Id}", entry.Id);
                try
                {
                    var deliveries = await consumer.DeliveryCountAsync(entry.Id);
                    if (deliveries >= MaxDeliveryAttempts)
                    {
                        await consumer.DeadLetterAsync(entry, $"delivered {deliveries} times without completing: {ex.Message}");
                    }
                }
                catch (Exception deadLetterEx)
                {
                    _logger.LogError(deadLetterEx, "Could not dead-letter output job {Id}; it stays pending", entry.Id);
                }
            }
            finally
            {
                inFlightByTenant.AddOrUpdate(tenantId, 0, (_, n) => Math.Max(0, n - 1));
                gate.Release();
            }
        }

        /// <summary>Re-claims the entry to this consumer, which resets its idle time.</summary>
        private Task KeepEntryAliveAsync(RedisValue entryId) =>
            _db.StreamClaimIdsOnlyAsync(
                FunctionWorkerQueueKeys.OutputsStream, FunctionQueueKeys.LogicWorkerGroup, _consumerName,
                minIdleTimeInMs: 0, messageIds: [entryId]);

        internal async Task<OutputDisposition> ProcessAsync(
            ResultStreamEntry entry, Func<Task>? keepEntryAlive, CancellationToken cancellationToken)
        {
            var runId = entry.Get("runId");
            var tenantId = entry.Get("tenantId");
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(tenantId)
                || !int.TryParse(entry.Get("attempt"), NumberStyles.None, CultureInfo.InvariantCulture, out var attempt)
                || attempt < 1)
            {
                _logger.LogError(
                    "Dropping output job {Id}: it needs runId, tenantId and a positive attempt (runId={RunId}, tenantId={TenantId}, attempt={Attempt})",
                    entry.Id, runId, tenantId, entry.Get("attempt"));
                return OutputDisposition.Done;
            }

            var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
            if (run is null)
            {
                _logger.LogWarning("Output job for run {RunId} dropped: no such run in tenant {TenantId}", runId, tenantId);
                return OutputDisposition.Done;
            }
            if (run.Attempt != attempt || run.Status != RunStatus.OutputProcessing)
            {
                // Already delivered (a duplicate job), closed by the stale-run sweeper, or a newer
                // attempt exists. In every case there is nothing left for this job to do.
                _logger.LogInformation(
                    "Output job for run {RunId} attempt {Attempt} has nothing to do: the run is {Status} at attempt {Current}",
                    runId, attempt, run.Status, run.Attempt);
                return OutputDisposition.Done;
            }

            var (actions, retryPolicy) = await FunctionOutputConfig.ResolveAsync(
                _versionRepository, _functionRepository, tenantId, run, cancellationToken);

            var results = new List<OutputActionResult>();
            var allSucceeded = true;
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (action is not { Enabled: true }) continue;

                var markerKey = FunctionWorkerQueueKeys.OutputActionMarker(
                    runId, attempt, $"{index.ToString(CultureInfo.InvariantCulture)}-{action.Id}");

                var step = await DeliverOnceAsync(
                    tenantId, run, action, retryPolicy, markerKey, keepEntryAlive, cancellationToken);
                if (step is null)
                {
                    return OutputDisposition.InFlightElsewhere;
                }

                results.AddRange(step.Value.Results);
                if (!step.Value.Ok)
                {
                    allSucceeded = false;
                    break;
                }
            }

            var finalStatus = allSucceeded ? RunStatus.Succeeded : RunStatus.OutputFailed;
            var written = await _runRepository.ApplyOutputResultAsync(
                tenantId, runId, attempt, results, finalStatus,
                allSucceeded ? null : "one or more output actions failed",
                cancellationToken);

            if (written)
            {
                await _db.PublishAsync(
                    RedisChannel.Literal(FunctionQueueKeys.SyncChannel(runId)), FunctionWireMapping.ToWire(finalStatus));
            }
            else
            {
                _logger.LogInformation(
                    "Output verdict for run {RunId} attempt {Attempt} not written: the run left OUTPUT_PROCESSING meanwhile",
                    runId, attempt);
            }

            return OutputDisposition.Done;
        }

        /// <summary>
        /// Sends one action at most once. Null when another live Worker holds its marker.
        /// </summary>
        private async Task<(IReadOnlyList<OutputActionResult> Results, bool Ok)?> DeliverOnceAsync(
            string tenantId, FunctionRunEntity run, OutputAction action, RetryPolicy retryPolicy,
            string markerKey, Func<Task>? keepEntryAlive, CancellationToken cancellationToken)
        {
            var inflight = Serialize(new ActionMarker { State = ActionMarker.InFlight, Owner = _consumerName, HeartbeatAt = DateTimeOffset.UtcNow });
            var acquired = await _db.StringSetAsync(
                markerKey, inflight, FunctionWorkerQueueKeys.OutputActionMarkerTtl, When.NotExists);

            if (!acquired)
            {
                return await FromExistingMarkerAsync(run, action, markerKey);
            }

            OutputActionChainResult chain;
            using (var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var heartbeat = HeartbeatAsync(markerKey, keepEntryAlive, heartbeatStop.Token);
                try
                {
                    chain = await _processor.ProcessAsync(tenantId, run, [action], retryPolicy, cancellationToken);
                }
                finally
                {
                    await heartbeatStop.CancelAsync();
                    await heartbeat;
                }
            }

            var done = new ActionMarker
            {
                State = ActionMarker.Done,
                Owner = _consumerName,
                HeartbeatAt = DateTimeOffset.UtcNow,
                Ok = chain.AllSucceeded,
                Results = [.. chain.Results],
            };
            await _db.StringSetAsync(markerKey, Serialize(done), FunctionWorkerQueueKeys.OutputActionMarkerTtl);

            return (chain.Results, chain.AllSucceeded);
        }

        private async Task<(IReadOnlyList<OutputActionResult> Results, bool Ok)?> FromExistingMarkerAsync(
            FunctionRunEntity run, OutputAction action, string markerKey)
        {
            var raw = await _db.StringGetAsync(markerKey);
            ActionMarker? marker = null;
            if (!raw.IsNullOrEmpty)
            {
                try
                {
                    marker = JsonSerializer.Deserialize<ActionMarker>((string)raw!, MarkerJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Output action marker {Key} is unreadable", markerKey);
                }
            }

            if (marker is { State: ActionMarker.Done })
            {
                return (marker.Results ?? [], marker.Ok);
            }

            if (marker is { State: ActionMarker.InFlight, HeartbeatAt: { } beat }
                && DateTimeOffset.UtcNow - beat < MarkerStaleAfter)
            {
                _logger.LogInformation(
                    "Output action {ActionId} of run {RunId} is being delivered by {Owner}; leaving the job to it",
                    action.Id, run.ItemId, marker.Owner);
                return null;
            }

            // The marker exists, so the action may already have been sent — by a Worker that then
            // stopped (or the marker vanished between SET NX and GET, or is unreadable). Sending
            // again could deliver it twice; not sending loses at most one delivery that a developer
            // can replay. The second is the only one that is recoverable.
            _logger.LogError(
                "Output action {ActionId} of run {RunId} was being delivered by {Owner}, which stopped; not re-sending",
                action.Id, run.ItemId, marker?.Owner ?? "an unknown Worker");
            return ([new OutputActionResult
            {
                ActionId = action.Id,
                Kind = action.Kind,
                Ok = false,
                Error = "delivery outcome unknown: the Worker sending this action stopped before recording the result, "
                    + "and it is not re-sent automatically to avoid a duplicate — replay the run to send it again",
                Attempts = 0,
            }], false);
        }

        private async Task HeartbeatAsync(string markerKey, Func<Task>? keepEntryAlive, CancellationToken stop)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(HeartbeatInterval, stop);
                    try
                    {
                        var beat = Serialize(new ActionMarker
                        {
                            State = ActionMarker.InFlight,
                            Owner = _consumerName,
                            HeartbeatAt = DateTimeOffset.UtcNow,
                        });
                        // XX: only ever refreshes this Worker's own marker, never re-creates one.
                        await _db.StringSetAsync(markerKey, beat, FunctionWorkerQueueKeys.OutputActionMarkerTtl, When.Exists);
                        if (keepEntryAlive is not null) await keepEntryAlive();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A missed beat is survivable; the next one may land.
                        _logger.LogWarning("Output action heartbeat for {Key} failed: {Message}", markerKey, ex.Message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped by the caller once the action finished.
            }
        }

        private static string Serialize(ActionMarker marker) => JsonSerializer.Serialize(marker, MarkerJson);

        internal sealed class ActionMarker
        {
            public const string InFlight = "inflight";
            public const string Done = "done";

            public string State { get; set; } = string.Empty;
            public string? Owner { get; set; }
            public DateTimeOffset? HeartbeatAt { get; set; }
            public bool Ok { get; set; }
            public List<OutputActionResult>? Results { get; set; }
        }
    }
}
