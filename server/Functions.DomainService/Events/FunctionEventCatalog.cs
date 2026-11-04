using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blocks.Genesis.Entities.Logic;
using Workflow.DomainService.Utils;

namespace Functions.DomainService.Events
{
    /// <summary>One event type a function can send.</summary>
    /// <param name="Type">The Genesis message type name (<c>typeof(T).Name</c>), which is also what Genesis routes by.</param>
    /// <param name="Queue">Where it goes. Fixed here: a function never picks a queue.</param>
    /// <param name="PublicAllowed">May a function with a Public trigger send it? A Public function sends no events (user, 2026-11-03).</param>
    public sealed record FunctionEventDefinition(
        string Type, string Title, string Description, bool PublicAllowed, string Queue);

    /// <summary>
    /// The events a function can send: one, fixed in code (<c>FunctionEvent</c>). Every event name maps to one message
    /// type and one queue; an unknown name is refused and never sent.
    /// <para>
    /// The payload is checked against the event's shape with <b>unknown fields refused</b>, then turned into the real
    /// message class by <see cref="BuildFunctionEvent"/>.
    /// </para>
    /// </summary>
    public static class FunctionEventCatalog
    {
        public const string FunctionEventType = "FunctionEvent";

        /// <summary>The most trigger event names one function can subscribe to.</summary>
        public const int MaxTriggerEvents = 20;

        /// <summary>The bound a payload is checked against again here, though the runner has checked it already.</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        private const int MaxFieldNameShown = 64;

        /// <summary>The name of a <c>FunctionEvent</c> and of a trigger event: the runtime and the runner use the same rule.</summary>
        public static readonly Regex NamePattern = new(
            @"^[a-z][a-z0-9_.-]{2,63}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

        public static IReadOnlyList<FunctionEventDefinition> All { get; } =
        [
            new(
                FunctionEventType,
                "Function event",
                "A tenant event, for example order.created. Starts the Live functions of this tenant that subscribe to its name.",
                PublicAllowed: false,
                LogicConstants.FunctionEventQueueName),
        ];

        public static FunctionEventDefinition? Find(string? type) =>
            All.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.Ordinal));

        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                return NamePattern.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Why <paramref name="payload"/> is not an acceptable payload for <paramref name="type"/>; null when it is.
        /// The reason names a field and a rule, never a value.
        /// </summary>
        public static string? Validate(string? type, JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object) return "the payload must be a JSON object";
            if (payload.GetRawText().Length > MaxPayloadBytes) return $"the payload is over {MaxPayloadBytes} characters";

            return type switch
            {
                FunctionEventType => ValidateFunctionEvent(payload),
                _ => "unknown event type",
            };
        }

        /// <inheritdoc cref="Validate(string?, JsonElement)"/>
        public static string? Validate(string? type, string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return "the payload must be a JSON object";
            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                return Validate(type, document.RootElement);
            }
            catch (JsonException)
            {
                return "the payload is not valid JSON";
            }
        }

        /// <summary>The real <see cref="FunctionEvent"/> for a payload that passed <see cref="Validate(string?, JsonElement)"/>.</summary>
        public static FunctionEvent BuildFunctionEvent(
            JsonElement payload, string eventId, string functionId, string runId, int depth) => new()
        {
            EventId = eventId,
            Name = payload.GetProperty("name").GetString() ?? string.Empty,
            DataJson = payload.TryGetProperty("data", out var data) ? data.GetRawText() : "null",
            SourceFunctionId = functionId,
            SourceRunId = runId,
            Depth = depth,
        };

        // ------------------------------------------------------------------ FunctionEvent ----

        private static string? ValidateFunctionEvent(JsonElement payload)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Name is not ("name" or "data"))
                {
                    return $"a FunctionEvent has only \"name\" and \"data\", not \"{Shown(property.Name)}\"";
                }
            }

            if (!payload.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || !IsValidName(name.GetString()))
            {
                return "the name must match ^[a-z][a-z0-9_.-]{2,63}$ (lowercase letters, digits, . _ -)";
            }
            return null;
        }

        /// <summary>A field name for a reason: clipped, because it comes from the function.</summary>
        private static string Shown(string name) =>
            name.Length <= MaxFieldNameShown ? name : string.Create(CultureInfo.InvariantCulture, $"{name[..MaxFieldNameShown]}...");
    }

    /// <summary>
    /// The depth stamped on a sent <see cref="FunctionEvent"/>: how many events deep the chain is.
    /// </summary>
    public static class FunctionEventDepth
    {
        /// <summary>
        /// The most events deep a chain may go: the event consumer refuses an event whose depth is over this
        /// (<c>DEPTH_LIMIT</c>), so A sends B sends A … stops.
        /// </summary>
        public const int MaxDepth = 5;

        /// <summary>
        /// What a run started by an event counts as when its starting event's depth is not known (the stored value
        /// expired or Redis was down): one hop.
        /// </summary>
        internal const int EventStartedRunDepth = 1;

        /// <summary>Depth of the events a run sends when its stored depth is not known: see the overload.</summary>
        public static int ForRun(FunctionRunEntity run) => ForRun(run, null);

        /// <summary>
        /// Depth of the events a run sends: 0 for a run that no event started; for one an event started
        /// (<see cref="InvokedByType.Event"/>), the depth of that starting event plus one
        /// (<paramref name="startingEventDepth"/>, stored when the event trigger created the run), or
        /// <see cref="EventStartedRunDepth"/> when it is not known.
        /// </summary>
        public static int ForRun(FunctionRunEntity run, int? startingEventDepth)
        {
            ArgumentNullException.ThrowIfNull(run);
            if (run.InvokedBy != InvokedByType.Event) return 0;
            return startingEventDepth is { } depth and >= 0 ? depth + 1 : EventStartedRunDepth;
        }
    }
}
