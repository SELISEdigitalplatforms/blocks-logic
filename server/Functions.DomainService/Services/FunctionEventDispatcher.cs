using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Functions.DomainService.Events;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Mail.DomainService.Mails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// What an outputs job says about the events of its run, copied from the result entry: where the runner stored
    /// them, how many, the runner's reason when it dropped them all, and the run's trace.
    /// </summary>
    public sealed record FunctionEventsHandoff(string? EventsKey, int Count, string? Rejected, string? TraceParent)
    {
        /// <summary>True when the run has no events and nothing to report about them.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(EventsKey) && string.IsNullOrEmpty(Rejected);

        /// <summary>Reads the fields <see cref="FunctionResultConsumer"/> copies onto the outputs job.</summary>
        public static FunctionEventsHandoff From(ResultStreamEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new FunctionEventsHandoff(
                entry.Get(FunctionQueueKeys.ResultEventsKeyField),
                entry.GetInt(FunctionQueueKeys.ResultEventCountField),
                entry.Get(FunctionQueueKeys.ResultEventsRejectedField),
                entry.Get(FunctionQueueKeys.TraceParentField));
        }
    }

    public interface IFunctionEventDispatcher
    {
        /// <summary>
        /// Sends the events of one succeeded run attempt and records what happened to each. Safe to call again for the
        /// same attempt: an event is never sent twice.
        /// </summary>
        /// <returns>False when another live Worker is in the middle of sending one of them: leave the job to it.</returns>
        Task<bool> DispatchAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Sends a run's events over the Genesis bus (<see cref="IMessageClient.SendToConsumerAsync{T}"/>) after the run
    /// succeeded, with the safeguards of a side effect that cannot be taken back.
    /// <list type="bullet">
    /// <item><b>Nothing the runner or the sandbox says is trusted.</b> Each event is checked again against the run's
    /// <i>version</i> list (narrowed to <c>PublicAllowed</c> types for a Public trigger) and the catalog's shape. A
    /// refused event is recorded as <c>Rejected</c> with its reason; the others go on.</item>
    /// <item><b>Identity comes from the run record</b> (tenant; the user id for information only; no roles; not
    /// authenticated, so Genesis writes no delegation grant). <c>ConsumerMessage.Context</c> is never set, so Genesis builds
    /// it from that context — a function cannot name another caller. The previous context is restored afterwards.</item>
    /// <item><b>At most once.</b> A SET NX marker per event id is taken before the send and holds its outcome after. A
    /// redelivered job reuses the outcome; a marker with no outcome (a Worker stopped mid-send) is reported
    /// <c>UNKNOWN_DELIVERY</c> and never re-sent, the same rule as output actions.</item>
    /// <item><b>Bounded.</b> Three attempts with a short pause, then <c>Failed</c> and an entry on
    /// <c>functions:dead-events</c> (no payload); a tenant sends at most <c>Functions:Events:PerTenantPerMinute</c>
    /// (600) events a minute.</item>
    /// </list>
    /// Payloads are never logged: only the event id, type, size and outcome.
    /// </summary>
    public sealed class FunctionEventDispatcher : IFunctionEventDispatcher
    {
        internal const int DefaultPerTenantPerMinute = 600;
        internal const int MaxSendAttempts = 3;
        internal const int MaxEventsPerRun = 50;
        private const int MaxIdChars = 256;
        private const int MaxTypeShown = 64;

        internal const string ErrorExpired = "EVENTS_EXPIRED";
        internal const string ErrorUnknownDelivery = "UNKNOWN_DELIVERY";
        internal const string ErrorRateLimited = "RATE_LIMITED";
        internal const string ErrorSendFailed = "SEND_FAILED";

        /// <summary>INCR, and EXPIRE on the first count, as one step: a crash between two commands cannot leave a counter that never expires.</summary>
        internal const string RateScript = @"
local count = redis.call('INCR', KEYS[1])
if count == 1 then redis.call('EXPIRE', KEYS[1], tonumber(ARGV[1])) end
return count";

        private static readonly TimeSpan RateWindowTtl = TimeSpan.FromMinutes(2);
        private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web);

        /// <summary>The pause after the first and the second failed send.</summary>
        internal TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)];

        /// <summary>A marker with no outcome that is older than this belongs to a Worker that stopped.</summary>
        internal TimeSpan MarkerStaleAfter { get; set; } = TimeSpan.FromMinutes(2);

        private readonly IDatabase _db;
        private readonly IMessageClient _messages;
        private readonly IFunctionRunEventsRepository _runEvents;
        private readonly IFunctionEventAllowList _allowList;
        private readonly int _perTenantPerMinute;
        private readonly ILogger<FunctionEventDispatcher> _logger;
        private readonly ActivitySource? _traces;
        private readonly IFunctionEventRunStore? _eventRuns;
        private readonly string _consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";

        public FunctionEventDispatcher(
            ICacheClient cache,
            IMessageClient messages,
            IFunctionRunEventsRepository runEvents,
            IFunctionEventAllowList allowList,
            IConfiguration configuration,
            ILogger<FunctionEventDispatcher> logger,
            ActivitySource? traces = null,
            IFunctionEventRunStore? eventRuns = null)
        {
            _eventRuns = eventRuns;
            ArgumentNullException.ThrowIfNull(cache);
            _db = cache.CacheDatabase();
            _messages = messages;
            _runEvents = runEvents;
            _allowList = allowList;
            _perTenantPerMinute = Math.Max(1, configuration.GetValue("Functions:Events:PerTenantPerMinute", DefaultPerTenantPerMinute));
            _logger = logger;
            _traces = traces;
        }

        public async Task<bool> DispatchAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(handoff);

            // The document is written last, so one that exists means every outcome is already recorded.
            if (await _runEvents.ExistsAsync(tenantId, run.ItemId, run.Attempt, cancellationToken)) return true;

            var document = new FunctionRunEventsEntity
            {
                RunId = run.ItemId,
                FunctionId = run.FunctionId,
                Attempt = run.Attempt,
                Rejected = string.IsNullOrEmpty(handoff.Rejected) ? null : handoff.Rejected,
                CreatedDate = DateTime.UtcNow,
            };

            if (!string.IsNullOrEmpty(handoff.EventsKey) && !await SendAllAsync(tenantId, run, handoff, document, cancellationToken))
            {
                return false;
            }

            await _runEvents.SaveAsync(tenantId, document, cancellationToken);

            // Every event has an outcome now; the key has nothing left to say.
            if (!string.IsNullOrEmpty(handoff.EventsKey)) await _db.KeyDeleteAsync(handoff.EventsKey);
            return true;
        }

        /// <returns>False when another live Worker holds one of the events.</returns>
        private async Task<bool> SendAllAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, FunctionRunEventsEntity document,
            CancellationToken cancellationToken)
        {
            // The key came off the entry; the only one a run may read is its own.
            if (handoff.EventsKey != FunctionQueueKeys.Events(run.ItemId))
            {
                _logger.LogError("Run {RunId}: the events key is not the run's own; no event is sent", run.ItemId);
                document.Rejected ??= "EVENTS_REJECTED: the events key does not belong to this run";
                return true;
            }

            var raw = await _db.StringGetAsync(handoff.EventsKey);
            if (raw.IsNullOrEmpty)
            {
                // Expired (or never written): the events are gone and are never sent late. The ids follow the runtime's own scheme.
                _logger.LogWarning("Run {RunId}: its {Count} event(s) expired before they were sent", run.ItemId, handoff.Count);
                for (var n = 1; n <= handoff.Count; n++)
                {
                    document.Events.Add(Failed(IdOf(run, n), string.Empty, ErrorExpired));
                }
                return true;
            }

            using var parsed = ParseEvents((string)raw!, document);
            if (parsed is null) return true;

            var allowed = await _allowList.AllowedTypesAsync(tenantId, run, cancellationToken);
            var prefix = $"{run.ItemId}-{run.Attempt}-";
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in parsed.RootElement.EnumerateArray())
            {
                index++;
                var entry = await HandleEventAsync(tenantId, run, handoff, item, index, prefix, allowed, seenIds, cancellationToken);
                if (entry is null) return false;
                document.Events.Add(entry);
            }
            return true;
        }

        /// <summary>The events array, or null (and a reason on <paramref name="document"/>) when it cannot be used.</summary>
        private JsonDocument? ParseEvents(string json, FunctionRunEventsEntity document)
        {
            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                document.Rejected ??= "EVENTS_REJECTED: the stored events are not valid JSON";
                return null;
            }

            if (parsed.RootElement.ValueKind != JsonValueKind.Array || parsed.RootElement.GetArrayLength() > MaxEventsPerRun)
            {
                parsed.Dispose();
                document.Rejected ??= $"EVENTS_REJECTED: the stored events are not a list of at most {MaxEventsPerRun}";
                return null;
            }
            return parsed;
        }

        /// <returns>The event's outcome; null when another live Worker holds it.</returns>
        private async Task<FunctionRunEventEntry?> HandleEventAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, JsonElement item, int index,
            string idPrefix, List<string> allowed, HashSet<string> seenIds, CancellationToken cancellationToken)
        {
            var problem = Inspect(item, idPrefix, allowed, seenIds, out var id, out var type, out var payload);
            if (problem is not null)
            {
                _logger.LogWarning("Run {RunId}: event {Index} rejected: {Reason}", run.ItemId, index, problem);
                return new FunctionRunEventEntry
                {
                    Id = id ?? IdOf(run, index),
                    Type = ShownType(type),
                    Status = FunctionEventStatus.Rejected,
                    Error = problem,
                };
            }

            var entry = new FunctionRunEventEntry { Id = id!, Type = type!, Name = NameOf(type!, payload) };
            if (await IsRateLimitedAsync(tenantId))
            {
                _logger.LogWarning("Tenant {TenantId}: event {EventId} refused, the per-minute limit is reached", tenantId, id);
                return Failed(entry, ErrorRateLimited);
            }

            return await SendOnceAsync(tenantId, run, handoff, entry, payload, cancellationToken);
        }

        /// <summary>Why <paramref name="item"/> may not be sent; null when it may.</summary>
        private static string? Inspect(
            JsonElement item, string idPrefix, List<string> allowed, HashSet<string> seenIds,
            out string? id, out string? type, out JsonElement payload)
        {
            id = null;
            type = null;
            payload = default;
            if (item.ValueKind != JsonValueKind.Object) return "the event is not an object";

            id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdChars || !id.StartsWith(idPrefix, StringComparison.Ordinal))
            {
                var bad = id;
                id = null;
                return bad is null ? "the event has no id" : "the event id does not belong to this run";
            }
            if (!seenIds.Add(id)) return "the event id is repeated";

            type = item.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
            if (type is null || !allowed.Contains(type)) return "the event type is not allowed for this function version";

            if (!item.TryGetProperty("payload", out payload)) return "the event has no payload";
            return FunctionEventCatalog.Validate(type, payload, forSend: true);
        }

        // ------------------------------------------------------------------ send once ----

        private async Task<FunctionRunEventEntry?> SendOnceAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, FunctionRunEventEntry entry,
            JsonElement payload, CancellationToken cancellationToken)
        {
            var markerKey = FunctionWorkerQueueKeys.EventSentMarker(entry.Id);
            var inFlight = new EventMarker { State = EventMarker.InFlight, Owner = _consumerName, At = DateTimeOffset.UtcNow };
            var acquired = await _db.StringSetAsync(
                markerKey, Serialize(inFlight), FunctionWorkerQueueKeys.EventSentMarkerTtl, When.NotExists);
            if (!acquired) return await FromExistingMarkerAsync(entry, markerKey);

            await SendWithRetriesAsync(tenantId, run, handoff, entry, payload, cancellationToken);

            var done = new EventMarker
            {
                State = EventMarker.Done,
                Owner = _consumerName,
                At = DateTimeOffset.UtcNow,
                Status = entry.Status,
                Error = entry.Error,
                Attempts = entry.Attempts,
                SentAt = entry.SentAt,
            };
            await _db.StringSetAsync(markerKey, Serialize(done), FunctionWorkerQueueKeys.EventSentMarkerTtl);
            return entry;
        }

        private async Task<FunctionRunEventEntry?> FromExistingMarkerAsync(FunctionRunEventEntry entry, string markerKey)
        {
            var raw = await _db.StringGetAsync(markerKey);
            EventMarker? marker = null;
            if (!raw.IsNullOrEmpty)
            {
                try
                {
                    marker = JsonSerializer.Deserialize<EventMarker>((string)raw!, MarkerJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Event marker {Key} is unreadable", markerKey);
                }
            }

            if (marker is { State: EventMarker.Done })
            {
                entry.Status = marker.Status;
                entry.Error = marker.Error;
                entry.Attempts = marker.Attempts;
                entry.SentAt = marker.SentAt;
                return entry;
            }

            if (marker is { State: EventMarker.InFlight, At: { } at } && DateTimeOffset.UtcNow - at < MarkerStaleAfter)
            {
                _logger.LogInformation("Event {EventId} is being sent by {Owner}; leaving the job to it", entry.Id, marker.Owner);
                return null;
            }

            // The marker exists, so the event may already have been sent by a Worker that then stopped (or the marker vanished,
            // or is unreadable). Sending again could deliver it twice; not sending loses at most one event the developer can
            // send again. Only the second is recoverable.
            _logger.LogError("Event {EventId} was being sent by {Owner}, which stopped; not sending it again", entry.Id, marker?.Owner ?? "an unknown Worker");
            return Failed(entry, ErrorUnknownDelivery);
        }

        private async Task SendWithRetriesAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, FunctionRunEventEntry entry,
            JsonElement payload, CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
            {
                entry.Attempts = attempt;
                try
                {
                    await SendOneAsync(tenantId, run, handoff, entry, payload);
                    entry.Status = FunctionEventStatus.Sent;
                    entry.Error = null;
                    entry.SentAt = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Event {EventId} ({Type}, {Bytes} bytes) of run {RunId} sent after {Attempts} attempt(s)",
                        entry.Id, entry.Type, payload.GetRawText().Length, run.ItemId, attempt);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The type only: the message of a bus error can quote the message it refused.
                    _logger.LogWarning(
                        "Event {EventId} ({Type}) of run {RunId}: send attempt {Attempt} of {Max} failed ({ExceptionType})",
                        entry.Id, entry.Type, run.ItemId, attempt, MaxSendAttempts, ex.GetType().Name);
                    entry.Error = $"{ErrorSendFailed}: {ex.GetType().Name}";
                }

                if (attempt < MaxSendAttempts) await Task.Delay(RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)], cancellationToken);
            }

            entry.Status = FunctionEventStatus.Failed;
            await DeadLetterAsync(tenantId, run, entry);
        }

        private async Task SendOneAsync(
            string tenantId, FunctionRunEntity run, FunctionEventsHandoff handoff, FunctionRunEventEntry entry, JsonElement payload)
        {
            using var emit = Common.InternalService.Tracing.RunTracing.Start(
                _traces, "Function::Emit", ActivityKind.Producer, handoff.TraceParent, tenantId);
            emit?.SetTag("blocks.tenant_id", tenantId);
            emit?.SetTag("blocks.function.id", run.FunctionId);
            emit?.SetTag("blocks.function.run_id", run.ItemId);
            emit?.SetTag("blocks.function.event_type", entry.Type);
            emit?.SetTag("blocks.function.event_id", entry.Id);

            var definition = FunctionEventCatalog.Find(entry.Type)
                ?? throw new InvalidOperationException("the event type is not in the catalog");

            // The run's own identity, set right before the send and put back after. Context on the message stays empty.
            var previous = BlocksContext.GetContext();
            try
            {
                BlocksContext.SetContext(RunContext(tenantId, run), true);
                if (entry.Type == FunctionEventCatalog.SendMailType)
                {
                    await _messages.SendToConsumerAsync(new ConsumerMessage<SendMail>
                    {
                        ConsumerName = definition.Queue,
                        Payload = FunctionEventCatalog.BuildSendMail(payload, entry.Id),
                    });
                }
                else
                {
                    await _messages.SendToConsumerAsync(new ConsumerMessage<FunctionEvent>
                    {
                        ConsumerName = definition.Queue,
                        Payload = FunctionEventCatalog.BuildFunctionEvent(
                            payload, entry.Id, run.FunctionId, run.ItemId, await DepthOfEventsSentByAsync(run)),
                    });
                }
            }
            finally
            {
                BlocksContext.SetContext(previous, previous is not null);
            }
        }

        /// <summary>
        /// The depth stamped on the events this run sends: for a run an event started, the depth stored when the run was
        /// created plus one (<see cref="FunctionEventDepth.ForRun(FunctionRunEntity, int?)"/>); 0 for any other run.
        /// </summary>
        private async Task<int> DepthOfEventsSentByAsync(FunctionRunEntity run)
        {
            if (run.InvokedBy != InvokedByType.Event || _eventRuns is null) return FunctionEventDepth.ForRun(run);

            var info = await _eventRuns.GetAsync(run.ItemId);
            return FunctionEventDepth.ForRun(run, info?.Depth);
        }

        /// <summary>
        /// The caller a sent event carries: the run's tenant, and the user id for information only when a user called
        /// the function. No roles, no permissions, not authenticated — so Genesis writes no delegation grant.
        /// </summary>
        internal static BlocksContext RunContext(string tenantId, FunctionRunEntity run) => BlocksContext.Create(
            tenantId: tenantId,
            roles: [],
            userId: run.InvokedBy is InvokedByType.Http or InvokedByType.Test ? run.InvokedById ?? string.Empty : string.Empty,
            isAuthenticated: false,
            requestUri: string.Empty,
            organizationId: string.Empty,
            expireOn: DateTime.MinValue,
            email: string.Empty,
            permissions: [],
            userName: string.Empty,
            phoneNumber: string.Empty,
            displayName: string.Empty,
            oauthToken: string.Empty,
            originalTenantId: tenantId,
            applicationDomain: string.Empty);

        // ------------------------------------------------------------ limits and dead letters ----

        private async Task<bool> IsRateLimitedAsync(string tenantId)
        {
            var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            var result = await _db.ScriptEvaluateAsync(
                RateScript, [FunctionWorkerQueueKeys.EventRate(tenantId, minute)], [(long)RateWindowTtl.TotalSeconds]);
            return !result.IsNull && (long)result > _perTenantPerMinute;
        }

        /// <summary>One entry per event that could not be sent: ids, type and reason, never the payload. Best effort.</summary>
        private async Task DeadLetterAsync(string tenantId, FunctionRunEntity run, FunctionRunEventEntry entry)
        {
            try
            {
                await _db.StreamAddAsync(FunctionQueueKeys.DeadEventsStream,
                [
                    new NameValueEntry("eventId", entry.Id),
                    new NameValueEntry("runId", run.ItemId),
                    new NameValueEntry("tenantId", tenantId),
                    new NameValueEntry("type", entry.Type),
                    new NameValueEntry("reason", entry.Error ?? ErrorSendFailed),
                    new NameValueEntry("failedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                ]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not add event {EventId} to the dead-event stream", entry.Id);
            }
        }

        // ------------------------------------------------------------------------ helpers ----

        private static string IdOf(FunctionRunEntity run, int index) =>
            string.Create(CultureInfo.InvariantCulture, $"{run.ItemId}-{run.Attempt}-{index}");

        private static string ShownType(string? type) =>
            type is null ? string.Empty : type.Length <= MaxTypeShown ? type : type[..MaxTypeShown];

        /// <summary>The name of a FunctionEvent from its (already checked) payload; empty for other types.</summary>
        private static string NameOf(string type, JsonElement payload) =>
            type == FunctionEventCatalog.FunctionEventType && payload.TryGetProperty("name", out var name)
                ? name.GetString() ?? string.Empty
                : string.Empty;

        private static FunctionRunEventEntry Failed(string id, string type, string error) =>
            new() { Id = id, Type = type, Status = FunctionEventStatus.Failed, Error = error };

        private static FunctionRunEventEntry Failed(FunctionRunEventEntry entry, string error)
        {
            entry.Status = FunctionEventStatus.Failed;
            entry.Error = error;
            return entry;
        }

        private static string Serialize(EventMarker marker) => JsonSerializer.Serialize(marker, MarkerJson);

        /// <summary>The send-once marker of one event: taken before the send, holding the outcome after.</summary>
        internal sealed class EventMarker
        {
            public const string InFlight = "inflight";
            public const string Done = "done";

            public string State { get; set; } = string.Empty;
            public string? Owner { get; set; }
            public DateTimeOffset? At { get; set; }
            public FunctionEventStatus Status { get; set; }
            public string? Error { get; set; }
            public int Attempts { get; set; }
            public DateTime? SentAt { get; set; }
        }
    }
}
