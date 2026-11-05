using System.Text;
using Blocks.Genesis;
using Functions.DomainService.Events;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using Workflow.DomainService.Actions;

namespace Functions.DomainService.Services
{
    /// <summary>The function side of the Action trigger: starts every function subscribed to a received action.</summary>
    public interface IFunctionActionTriggerService
    {
        /// <summary>Never throws: whatever cannot be started is dead-lettered.</summary>
        Task StartAsync(BlocksAction action);
    }

    /// <summary>
    /// The Action trigger for functions: a <see cref="BlocksAction"/> from Genesis (<c>blocks_logic_action_function_listener</c>)
    /// starts the functions that subscribe to it.
    /// <list type="bullet">
    /// <item><b>The tenant is the message's context</b> (Genesis rebuilds <see cref="BlocksContext"/> from the message),
    /// never a payload field. The runs use that same context as their identity.</item>
    /// <item><b>Switch re-checked:</b> the tenant's <c>actions</c> list is read again; an action that is missing or whose
    /// Function switch is off is dropped (it may have been turned off after Genesis sent it).</item>
    /// <item><b>Subscribers:</b> every Live function whose <i>active</i> version lists the action in its <c>TriggerEvents</c>.
    /// No cap; the lookup is paged so memory stays flat.</item>
    /// <item><b>Input:</b> the simple JSON <c>{ id, action, time, organizationId, data }</c> (<see cref="ActionTriggerData"/>).</item>
    /// <item><b>Never throws.</b> Genesis swallows a consumer's exception (the message is lost), so retry and dead-letter
    /// happen here: each start is tried <see cref="MaxInvokeAttempts"/> times with a short pause, then dead-lettered to
    /// <c>functions:dead-function-events</c> (ids and a reason, never the payload). A run that later fails for good is
    /// dead-lettered by the Worker's result path.</item>
    /// <item><b>Redelivery is safe:</b> one run per event and function (see
    /// <see cref="IFunctionInvocationService.InvokeFromEventAsync"/>).</item>
    /// </list>
    /// </summary>
    public sealed class FunctionActionTriggerService : IFunctionActionTriggerService
    {
        /// <summary>Tries to start one function for one event.</summary>
        internal const int MaxInvokeAttempts = 3;

        /// <summary>Subscribed versions read per page.</summary>
        internal const int SubscriptionPageSize = 100;

        private const int MaxEventIdChars = 256;

        private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];

        private readonly IFunctionEventSettingsRepository _settings;
        private readonly IFunctionRepository _functions;
        private readonly IFunctionInvocationService _invocation;
        private readonly IFunctionEventDeadLetters _deadLetters;
        private readonly IActionsRepository _actions;
        private readonly ILogger<FunctionActionTriggerService> _logger;

        /// <summary>The pause between tries; a test seam.</summary>
        internal Func<TimeSpan, Task> Pause { get; set; } = delay => Task.Delay(delay);

        public FunctionActionTriggerService(
            IFunctionEventSettingsRepository settings,
            IFunctionRepository functions,
            IFunctionInvocationService invocation,
            IFunctionEventDeadLetters deadLetters,
            IActionsRepository actions,
            ILogger<FunctionActionTriggerService> logger)
        {
            _settings = settings;
            _functions = functions;
            _invocation = invocation;
            _deadLetters = deadLetters;
            _actions = actions;
            _logger = logger;
        }

        public async Task StartAsync(BlocksAction action)
        {
            try
            {
                await HandleAsync(action);
            }
            catch (Exception ex)
            {
                // Whatever went wrong, the message is gone after this (Genesis swallows it): record it instead of losing it.
                _logger.LogError(ex, "Action {EventId} could not be handled for functions", action?.Id);
                await _deadLetters.AddAsync(
                    action?.Id ?? string.Empty, string.Empty, string.Empty, BlocksContext.GetContext()?.TenantId ?? string.Empty,
                    ShownName(action?.Action), $"{FunctionEventDeadReasons.InvokeFailed}:{ex.GetType().Name}");
            }
        }

        private async Task HandleAsync(BlocksAction? action)
        {
            var context = BlocksContext.GetContext();
            var tenantId = context?.TenantId ?? string.Empty;
            if (action is null)
            {
                _logger.LogWarning("An empty action was received");
                await _deadLetters.AddAsync(string.Empty, string.Empty, string.Empty, tenantId, string.Empty, FunctionEventDeadReasons.InvalidMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("Action {EventId} has no tenant in its context; dropped", action.Id);
                await _deadLetters.AddAsync(action.Id, string.Empty, string.Empty, tenantId, ShownName(action.Action), FunctionEventDeadReasons.NoTenant);
                return;
            }

            var problem = Check(action);
            if (problem is not null)
            {
                _logger.LogWarning("Action {EventId} in tenant {TenantId} is invalid: {Problem}", action.Id, tenantId, problem);
                await _deadLetters.AddAsync(
                    action.Id, string.Empty, string.Empty, tenantId, ShownName(action.Action), FunctionEventDeadReasons.InvalidMessage);
                return;
            }

            // The switch may have been turned off after Genesis sent it.
            var definition = await _actions.FindAsync(action.Action);
            if (definition is not { Function: true })
            {
                _logger.LogInformation("Action {Action} ({EventId}) is not on for functions in tenant {TenantId}; dropped",
                    action.Action, action.Id, tenantId);
                return;
            }

            var invocation = new FunctionEventInvocation(action.Id, action.Action, ActionTriggerData.ToJson(action));
            var started = 0;
            FunctionEventSubscription? after = null;
            while (true)
            {
                var page = await FindPageWithRetriesAsync(tenantId, action, after);
                foreach (var functionId in await LiveSubscribersAsync(tenantId, page))
                {
                    await StartOneAsync(tenantId, functionId, invocation, context);
                    started++;
                }

                if (page.Count < SubscriptionPageSize) break;
                after = page[^1];
            }

            _logger.LogInformation("Action {Action} ({EventId}) reached {Count} functions in tenant {TenantId}",
                action.Action, action.Id, started, tenantId);
        }

        /// <summary>Why the action cannot be used; null when it can. Never contains payload text.</summary>
        private static string? Check(BlocksAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Id) || action.Id.Length > MaxEventIdChars) return "the event id is missing or too long";
            if (!FunctionEventCatalog.IsValidName(action.Action)) return "the action name is not valid";
            if (Encoding.UTF8.GetByteCount(action.DataJson ?? string.Empty) > FunctionEventCatalog.MaxPayloadBytes) return "the data is over the size limit";
            return null;
        }

        /// <summary>The name, only when it is a valid one: a bad name is not echoed to the stream.</summary>
        private static string ShownName(string? name) => FunctionEventCatalog.IsValidName(name) ? name! : string.Empty;

        /// <summary>One lookup page, tried <see cref="MaxInvokeAttempts"/> times; a database that stays down surfaces to <see cref="StartAsync"/>, which dead-letters the event.</summary>
        private async Task<IReadOnlyList<FunctionEventSubscription>> FindPageWithRetriesAsync(
            string tenantId, BlocksAction action, FunctionEventSubscription? after)
        {
            var attempt = 1;
            while (true)
            {
                try
                {
                    return await _settings.FindSubscriptionsAsync(tenantId, action.Action, after, SubscriptionPageSize);
                }
                catch (Exception ex) when (attempt < MaxInvokeAttempts)
                {
                    _logger.LogWarning( // NOSONAR S6667 — type only: a driver error can quote query values
                        "Looking up the subscribers of action {EventId} failed (try {Attempt} of {Max}): {ExceptionType}",
                        action.Id, attempt, MaxInvokeAttempts, ex.GetType().Name);
                    await Pause(Backoff[Math.Min(attempt - 1, Backoff.Length - 1)]);
                    attempt++;
                }
            }
        }

        /// <summary>Ids of the page's functions to start: Live, with their active version among the page's subscribed versions.</summary>
        private async Task<List<string>> LiveSubscribersAsync(string tenantId, IReadOnlyList<FunctionEventSubscription> page)
        {
            if (page.Count == 0) return [];

            var versionsByFunction = page
                .GroupBy(c => c.FunctionId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.VersionId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

            var live = await _functions.GetLiveByIdsAsync(tenantId, [.. versionsByFunction.Keys]);
            var eligible = live
                .Where(f => !string.IsNullOrEmpty(f.ActiveVersionId)
                    && versionsByFunction.TryGetValue(f.ItemId, out var versions)
                    && versions.Contains(f.ActiveVersionId))
                .Select(f => f.ItemId)
                .ToList();
            eligible.Sort(StringComparer.Ordinal);
            return eligible;
        }

        /// <summary>Starts one function; up to <see cref="MaxInvokeAttempts"/> tries, then the dead-letter stream.</summary>
        private async Task StartOneAsync(string tenantId, string functionId, FunctionEventInvocation invocation, BlocksContext? context)
        {
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
                    _logger.LogInformation( // NOSONAR S6667 — expected outcome, the type is the whole story
                        "Function {FunctionId} no longer takes action {EventId} ({Name}): {ExceptionType}",
                        functionId, invocation.EventId, invocation.Name, ex.GetType().Name);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    _logger.LogWarning( // NOSONAR S6667 — type only: the error can quote the input; the last one is dead-lettered by type
                        "Starting function {FunctionId} for action {EventId} failed (try {Attempt} of {Max}): {ExceptionType}",
                        functionId, invocation.EventId, attempt, MaxInvokeAttempts, ex.GetType().Name);
                    if (attempt < MaxInvokeAttempts) await Pause(Backoff[Math.Min(attempt - 1, Backoff.Length - 1)]);
                }
            }

            await _deadLetters.AddAsync(
                invocation.EventId, functionId, string.Empty, tenantId, invocation.Name,
                $"{FunctionEventDeadReasons.InvokeFailed}:{last?.GetType().Name}");
        }
    }
}
