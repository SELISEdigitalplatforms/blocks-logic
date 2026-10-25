using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// The event trigger: receives the tenant's <see cref="FunctionEvent"/>s from the Genesis bus
    /// (<c>blocks_logic_function_event_listener</c>) and starts the functions that subscribe to them.
    /// <list type="bullet">
    /// <item><b>The tenant is the message's context</b> (Genesis rebuilds <see cref="BlocksContext"/> from the message),
    /// never a payload field. The runs use that same context as their identity.</item>
    /// <item><b>Subscribers:</b> the tenant's Live functions whose <i>active</i> version lists the event's name in its
    /// <c>TriggerEvents</c>; never the sender itself (no self-trigger); at most <see cref="MaxSubscribers"/>, first by
    /// function id — the rest are logged as skipped.</item>
    /// <item><b>Checked first:</b> id, name, data (valid JSON, at most 64 KB) and depth (at most
    /// <see cref="FunctionEventDepth.MaxDepth"/>). A bad event is logged and dead-lettered, never run.</item>
    /// <item><b>Never throws.</b> Genesis swallows a consumer's exception (the message is lost), so retry and dead-letter
    /// happen here: each start is tried <see cref="MaxInvokeAttempts"/> times with a short pause, then dead-lettered to
    /// <c>functions:dead-function-events</c> (ids and a reason, never the payload). A run that later fails for good is
    /// dead-lettered by the Worker's result path.</item>
    /// <item><b>Redelivery is safe:</b> one run per event and function (see
    /// <see cref="IFunctionInvocationService.InvokeFromEventAsync"/>).</item>
    /// </list>
    /// </summary>
    public sealed class FunctionEventConsumer : IConsumer<FunctionEvent>
    {
        /// <summary>Most functions one event starts.</summary>
        internal const int MaxSubscribers = 10;

        /// <summary>Tries to start one function for one event.</summary>
        internal const int MaxInvokeAttempts = 3;

        /// <summary>Most subscribed versions read for one event (a function can have several versions listed).</summary>
        internal const int MaxCandidates = 1000;

        private const int FunctionLookupBatch = 100;
        private const int MaxEventIdChars = 256;

        private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];

        private readonly IFunctionEventSettingsRepository _settings;
        private readonly IFunctionRepository _functions;
        private readonly IFunctionInvocationService _invocation;
        private readonly IFunctionEventDeadLetters _deadLetters;
        private readonly ILogger<FunctionEventConsumer> _logger;

        /// <summary>The pause between tries; a test seam.</summary>
        internal Func<TimeSpan, Task> Pause { get; set; } = delay => Task.Delay(delay);

        public FunctionEventConsumer(
            IFunctionEventSettingsRepository settings,
            IFunctionRepository functions,
            IFunctionInvocationService invocation,
            IFunctionEventDeadLetters deadLetters,
            ILogger<FunctionEventConsumer> logger)
        {
            _settings = settings;
            _functions = functions;
            _invocation = invocation;
            _deadLetters = deadLetters;
            _logger = logger;
        }

        public async Task Consume(FunctionEvent message)
        {
            try
            {
                await HandleAsync(message);
            }
            catch (Exception ex)
            {
                // Whatever went wrong, the message is gone after this (Genesis swallows it): record it instead of losing it.
                _logger.LogError(ex, "Function event {EventId} could not be handled", message?.EventId);
                await _deadLetters.AddAsync(
                    message?.EventId ?? string.Empty, string.Empty, string.Empty, BlocksContext.GetContext()?.TenantId ?? string.Empty,
                    message?.Name ?? string.Empty, $"{FunctionEventDeadReasons.InvokeFailed}:{ex.GetType().Name}");
            }
        }

        private async Task HandleAsync(FunctionEvent message)
        {
            var context = BlocksContext.GetContext();
            var tenantId = context?.TenantId ?? string.Empty;
            var name = message?.Name ?? string.Empty;
            if (message is null)
            {
                _logger.LogWarning("An empty function event was received");
                await _deadLetters.AddAsync(string.Empty, string.Empty, string.Empty, tenantId, name, FunctionEventDeadReasons.InvalidMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("Function event {EventId} has no tenant in its context; dropped", message.EventId);
                await _deadLetters.AddAsync(message.EventId, string.Empty, string.Empty, tenantId, name, FunctionEventDeadReasons.NoTenant);
                return;
            }

            var problem = Check(message);
            if (problem is not null)
            {
                _logger.LogWarning("Function event {EventId} in tenant {TenantId} is invalid: {Problem}", message.EventId, tenantId, problem);
                await _deadLetters.AddAsync(
                    message.EventId, string.Empty, string.Empty, tenantId, ShownName(name), FunctionEventDeadReasons.InvalidMessage);
                return;
            }

            if (message.Depth > FunctionEventDepth.MaxDepth)
            {
                _logger.LogWarning(
                    "Function event {EventId} ({Name}) in tenant {TenantId} is {Depth} events deep, over the limit of {Max}; not run",
                    message.EventId, name, tenantId, message.Depth, FunctionEventDepth.MaxDepth);
                await _deadLetters.AddAsync(message.EventId, string.Empty, string.Empty, tenantId, name, FunctionEventDeadReasons.DepthLimit);
                return;
            }

            var subscribers = await FindSubscribersWithRetriesAsync(tenantId, message);
            if (subscribers.Count == 0)
            {
                _logger.LogDebug("Function event {EventId} ({Name}) has no subscribers in tenant {TenantId}", message.EventId, name, tenantId);
                return;
            }

            foreach (var functionId in subscribers)
            {
                await StartAsync(tenantId, functionId, message, context);
            }
        }

        /// <summary>Why the event cannot be used; null when it can. Never contains payload text.</summary>
        private static string? Check(FunctionEvent message)
        {
            if (string.IsNullOrWhiteSpace(message.EventId) || message.EventId.Length > MaxEventIdChars) return "the event id is missing or too long";
            if (!FunctionEventCatalog.IsValidName(message.Name)) return "the event name is not valid";
            if (message.Depth < 0) return "the depth is negative";

            var data = message.DataJson;
            if (string.IsNullOrEmpty(data)) return "the data is missing";
            if (Encoding.UTF8.GetByteCount(data) > FunctionEventCatalog.MaxPayloadBytes) return "the data is over the size limit";
            try
            {
                using var _ = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                return "the data is not valid JSON";
            }
            return null;
        }

        /// <summary>The name, only when it is a valid one: a bad name came from the sender and is not echoed to the stream.</summary>
        private static string ShownName(string name) => FunctionEventCatalog.IsValidName(name) ? name : string.Empty;

        /// <summary>The lookup, tried <see cref="MaxInvokeAttempts"/> times; a database that stays down surfaces to <see cref="Consume"/>, which dead-letters the event.</summary>
        private async Task<List<string>> FindSubscribersWithRetriesAsync(string tenantId, FunctionEvent message)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await FindSubscribersAsync(tenantId, message);
                }
                catch (Exception ex) when (attempt < MaxInvokeAttempts)
                {
                    _logger.LogWarning(
                        "Looking up the subscribers of event {EventId} failed (try {Attempt} of {Max}): {ExceptionType}",
                        message.EventId, attempt, MaxInvokeAttempts, ex.GetType().Name);
                    await Pause(Backoff[Math.Min(attempt - 1, Backoff.Length - 1)]);
                }
            }
        }

        /// <summary>
        /// Ids of the functions to start: Live, with their active version subscribed to the name, not the sender, the
        /// first <see cref="MaxSubscribers"/> by id.
        /// </summary>
        private async Task<List<string>> FindSubscribersAsync(string tenantId, FunctionEvent message)
        {
            var candidates = await _settings.FindSubscriptionsAsync(tenantId, message.Name, MaxCandidates);
            var versionsByFunction = candidates
                .Where(c => !string.Equals(c.FunctionId, message.SourceFunctionId, StringComparison.Ordinal))
                .GroupBy(c => c.FunctionId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.VersionId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

            var eligible = new List<string>();
            foreach (var batch in versionsByFunction.Keys.Chunk(FunctionLookupBatch))
            {
                var live = await _functions.GetLiveByIdsAsync(tenantId, batch);
                eligible.AddRange(live
                    .Where(f => !string.IsNullOrEmpty(f.ActiveVersionId)
                        && versionsByFunction[f.ItemId].Contains(f.ActiveVersionId))
                    .Select(f => f.ItemId));
            }

            eligible.Sort(StringComparer.Ordinal);
            if (eligible.Count > MaxSubscribers)
            {
                _logger.LogWarning(
                    "Function event {EventId} ({Name}) has {Count} subscribers; starting the first {Max} by function id and skipping {Skipped}: {SkippedIds}",
                    message.EventId, message.Name, eligible.Count, MaxSubscribers, eligible.Count - MaxSubscribers,
                    string.Join(",", eligible.Skip(MaxSubscribers)));
            }
            return [.. eligible.Take(MaxSubscribers)];
        }

        /// <summary>Starts one function; up to <see cref="MaxInvokeAttempts"/> tries, then the dead-letter stream.</summary>
        private async Task StartAsync(string tenantId, string functionId, FunctionEvent message, BlocksContext? context)
        {
            var invocation = new FunctionEventInvocation(message.EventId, message.Name, message.DataJson, message.Depth);
            Exception? last = null;
            for (var attempt = 1; attempt <= MaxInvokeAttempts; attempt++)
            {
                try
                {
                    await _invocation.InvokeFromEventAsync(tenantId, functionId, invocation, context);
                    return;
                }
                catch (Exception ex) when (ex is FunctionAuthorizationException or FunctionNotFoundException)
                {
                    // It stopped subscribing, or went away, between the lookup and the start. Nothing to retry.
                    _logger.LogInformation(
                        "Function {FunctionId} no longer takes event {EventId} ({Name}): {ExceptionType}",
                        functionId, message.EventId, message.Name, ex.GetType().Name);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    _logger.LogWarning(
                        "Starting function {FunctionId} for event {EventId} failed (try {Attempt} of {Max}): {ExceptionType}",
                        functionId, message.EventId, attempt, MaxInvokeAttempts, ex.GetType().Name);
                    if (attempt < MaxInvokeAttempts) await Pause(Backoff[Math.Min(attempt - 1, Backoff.Length - 1)]);
                }
            }

            await _deadLetters.AddAsync(
                message.EventId, functionId, string.Empty, tenantId, message.Name,
                $"{FunctionEventDeadReasons.InvokeFailed}:{last?.GetType().Name}");
        }
    }
}
