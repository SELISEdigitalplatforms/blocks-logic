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
            await consumer.EnsureGroupAsync().ConfigureAwait(false);

            // The capacity logged here is the budget's current view, not a configured number:
            // it moves with the host while this loop runs.
            _logger.LogInformation(
                "Consuming {Stream} as {Consumer} (capacity {Capacity})",
                RedisKeys.RunsStream, _options.RunnerId, _budget.Capacity);

            var idleDelay = TimeSpan.FromMilliseconds(250);
            var reclaimEvery = TimeSpan.FromSeconds(30);
            var nextReclaim = DateTimeOffset.UtcNow.Add(reclaimEvery);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Never claim work this host cannot safely execute.
                    var readiness = _heartbeat.Latest;
                    if (readiness is { Healthy: false })
                    {
                        _logger.LogWarning("Not claiming work — the host is unhealthy: {Summary}", readiness.Summary);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    var entries = await consumer.ReadNewAsync(count: 1, stoppingToken).ConfigureAwait(false);

                    if (entries.Count == 0 && DateTimeOffset.UtcNow >= nextReclaim)
                    {
                        nextReclaim = DateTimeOffset.UtcNow.Add(reclaimEvery);
                        entries = await consumer.ReclaimAbandonedAsync(
                            TimeSpan.FromMilliseconds(_options.ClaimIdleMs), count: 5).ConfigureAwait(false);
                    }

                    if (entries.Count == 0)
                    {
                        await Task.Delay(idleDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await HandleAsync(consumer, entry, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
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

            _logger.LogInformation("Run loop stopped");
        }

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
            var deliveries = await consumer.DeliveryCountAsync(entry.Id).ConfigureAwait(false);
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

            var disposition = await _processor.ProcessAsync(job, token).ConfigureAwait(false);

            switch (disposition)
            {
                case RunProcessor.Disposition.Complete:
                    // Acknowledged last, and only now: the result is already durable.
                    await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
                    break;

                case RunProcessor.Disposition.Deferred:
                    // Left pending on purpose. Backing off keeps a full host from spinning.
                    await Task.Delay(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
                    break;

                case RunProcessor.Disposition.NotOurs:
                    // Another runner owns it; do not acknowledge someone else's work.
                    break;

                default:
                    break;
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
