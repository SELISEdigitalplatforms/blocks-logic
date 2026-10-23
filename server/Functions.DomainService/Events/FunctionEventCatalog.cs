using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blocks.Genesis.Entities.Logic;
using Mail.DomainService.Mails;
using Workflow.DomainService.Utils;

namespace Functions.DomainService.Events
{
    /// <summary>One event a function may be allowed to send.</summary>
    /// <param name="Type">The Genesis message type name (<c>typeof(T).Name</c>), which is also what Genesis routes by.</param>
    /// <param name="Queue">Where it goes. Fixed here: a function never picks a queue.</param>
    /// <param name="PublicAllowed">May a function with a Public trigger send it? Mail is allowed on purpose (user, 2026-10-23).</param>
    /// <param name="PayloadTemplateJson">The payload as the editor fills it in, camelCase.</param>
    public sealed record FunctionEventDefinition(
        string Type, string Title, string Description, bool PublicAllowed, string Queue, string PayloadTemplateJson);

    /// <summary>
    /// The events a function can send: two, fixed in code (<c>SendMail</c> and <c>FunctionEvent</c>). Every event name
    /// maps to one message type and one queue; an unknown name is refused and never sent.
    /// <para>
    /// The payload is checked against the event's shape with <b>unknown fields refused</b>, then turned into the real
    /// message class by <see cref="BuildSendMail"/> / <see cref="BuildFunctionEvent"/>. Fields the platform owns are
    /// never read from the payload: for mail <c>SendPhoneNumberAsEmail</c> is forced off (so only tenant users receive
    /// mail), <c>CorrelationId</c> is the event id and <c>ReplyTo</c> is not offered.
    /// </para>
    /// </summary>
    public static class FunctionEventCatalog
    {
        public const string SendMailType = "SendMail";
        public const string FunctionEventType = "FunctionEvent";

        /// <summary>The most trigger event names one function can subscribe to.</summary>
        public const int MaxTriggerEvents = 20;

        /// <summary>The bound a payload is checked against again here, though the runner has checked it already.</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        private const int MaxRecipients = 100;
        private const int MaxAttachments = 10;
        private const int MaxContextEntries = 100;
        private const int MaxShortText = 320;
        private const int MaxContextValue = 10_000;
        private const int MaxFieldNameShown = 64;

        /// <summary>The name of a <c>FunctionEvent</c> and of a trigger event: the runtime and the runner use the same rule.</summary>
        public static readonly Regex NamePattern = new(
            @"^[a-z][a-z0-9_.-]{2,63}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

        private static readonly string[] MailTextFields = ["purpose", "language"];
        private static readonly string[] MailListFields = ["to", "cc", "bcc", "attachments"];
        private static readonly string[] MailMapFields = ["subjectDataContext", "bodyDataContext"];

        public static IReadOnlyList<FunctionEventDefinition> All { get; } =
        [
            new(
                SendMailType,
                "Send mail",
                "Sends a mail from a template (purpose and language) to users of this tenant.",
                PublicAllowed: true,
                LogicConstants.LogicMailQueueName,
                """{"purpose":"","language":"en","to":[],"cc":[],"bcc":[],"subjectDataContext":{},"bodyDataContext":{},"attachments":[]}"""),
            new(
                FunctionEventType,
                "Function event",
                "A tenant event, for example order.created. Starts the Live functions of this tenant that subscribe to its name.",
                PublicAllowed: false,
                LogicConstants.FunctionEventQueueName,
                """{"name":"order.created","data":{}}"""),
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
        /// <param name="forSend">
        /// True for a payload that is about to be sent: a mail then also needs a purpose and a recipient. False for a
        /// payload template in the event settings, which may still be blank.
        /// </param>
        public static string? Validate(string? type, JsonElement payload, bool forSend)
        {
            if (payload.ValueKind != JsonValueKind.Object) return "the payload must be a JSON object";
            if (payload.GetRawText().Length > MaxPayloadBytes) return $"the payload is over {MaxPayloadBytes} characters";

            return type switch
            {
                SendMailType => ValidateSendMail(payload, forSend),
                FunctionEventType => ValidateFunctionEvent(payload),
                _ => "unknown event type",
            };
        }

        /// <inheritdoc cref="Validate(string?, JsonElement, bool)"/>
        public static string? Validate(string? type, string? payloadJson, bool forSend)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return "the payload must be a JSON object";
            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                return Validate(type, document.RootElement, forSend);
            }
            catch (JsonException)
            {
                return "the payload is not valid JSON";
            }
        }

        /// <summary>The real <see cref="SendMail"/> for a payload that passed <see cref="Validate(string?, JsonElement, bool)"/>.</summary>
        public static SendMail BuildSendMail(JsonElement payload, string eventId)
        {
            var mail = new SendMail
            {
                Purpose = string.Empty,
                Language = "en",
                To = [],
                // Only tenant users receive mail: a phone number is never used as an address. Not a payload field.
                SendPhoneNumberAsEmail = false,
                CorrelationId = eventId,
            };

            foreach (var property in payload.EnumerateObject())
            {
                switch (property.Name.ToLowerInvariant())
                {
                    case "purpose": mail.Purpose = property.Value.GetString() ?? string.Empty; break;
                    case "language": mail.Language = property.Value.GetString() is { Length: > 0 } l ? l : "en"; break;
                    case "to": mail.To = ReadStrings(property.Value); break;
                    case "cc": mail.Cc = ReadStrings(property.Value); break;
                    case "bcc": mail.Bcc = ReadStrings(property.Value); break;
                    case "attachments": mail.Attachments = ReadStrings(property.Value); break;
                    case "subjectdatacontext": mail.SubjectDataContext = ReadMap(property.Value); break;
                    case "bodydatacontext": mail.BodyDataContext = ReadMap(property.Value); break;
                    default: break;
                }
            }

            return mail;
        }

        /// <summary>The real <see cref="FunctionEvent"/> for a payload that passed <see cref="Validate(string?, JsonElement, bool)"/>.</summary>
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

        // ---------------------------------------------------------------------- SendMail ----

        private static string? ValidateSendMail(JsonElement payload, bool forSend)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recipients = 0;
            foreach (var property in payload.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return $"the field \"{Shown(property.Name)}\" is repeated";

                var error = ValidateMailField(property);
                if (error is not null) return error;

                if (IsRecipientField(property.Name)) recipients += property.Value.GetArrayLength();
            }

            if (recipients > MaxRecipients) return $"a mail can have at most {MaxRecipients} recipients";
            return forSend ? ValidateMailForSend(payload) : null;
        }

        private static string? ValidateMailForSend(JsonElement payload)
        {
            var purpose = FindField(payload, "purpose");
            if (purpose is not { ValueKind: JsonValueKind.String } || string.IsNullOrWhiteSpace(purpose.Value.GetString()))
            {
                return "the field \"purpose\" is required";
            }
            var to = FindField(payload, "to");
            return to is { ValueKind: JsonValueKind.Array } && to.Value.GetArrayLength() > 0
                ? null
                : "the field \"to\" needs at least one recipient";
        }

        private static JsonElement? FindField(JsonElement payload, string name)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }

        private static bool IsRecipientField(string name) =>
            name.Equals("to", StringComparison.OrdinalIgnoreCase)
            || name.Equals("cc", StringComparison.OrdinalIgnoreCase)
            || name.Equals("bcc", StringComparison.OrdinalIgnoreCase);

        private static string? ValidateMailField(JsonProperty property)
        {
            var name = property.Name;
            if (MailTextFields.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String && property.Value.GetString()!.Length <= MaxShortText
                    ? null
                    : $"the field \"{Shown(name)}\" must be a text of at most {MaxShortText} characters";
            }
            if (MailListFields.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return ValidateStringList(property, IsRecipientField(name) ? MaxRecipients : MaxAttachments);
            }
            if (MailMapFields.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return ValidateMap(property);
            }
            return $"unknown field \"{Shown(name)}\"";
        }

        private static string? ValidateStringList(JsonProperty property, int max)
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                return $"the field \"{Shown(property.Name)}\" must be a list of texts";
            }
            if (property.Value.GetArrayLength() > max)
            {
                return $"the field \"{Shown(property.Name)}\" can hold at most {max} items";
            }
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString()!.Length > MaxShortText)
                {
                    return $"the field \"{Shown(property.Name)}\" must hold texts of at most {MaxShortText} characters";
                }
            }
            return null;
        }

        private static string? ValidateMap(JsonProperty property)
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                return $"the field \"{Shown(property.Name)}\" must be an object";
            }

            var entries = 0;
            foreach (var entry in property.Value.EnumerateObject())
            {
                if (++entries > MaxContextEntries)
                {
                    return $"the field \"{Shown(property.Name)}\" can hold at most {MaxContextEntries} entries";
                }
                if (!IsScalar(entry.Value) || ScalarText(entry.Value).Length > MaxContextValue)
                {
                    return $"the values of \"{Shown(property.Name)}\" must be texts, numbers or true/false (at most {MaxContextValue} characters)";
                }
            }
            return null;
        }

        private static bool IsScalar(JsonElement value) =>
            value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

        private static string ScalarText(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };

        private static List<string> ReadStrings(JsonElement array) =>
            [.. array.EnumerateArray().Select(e => e.GetString() ?? string.Empty)];

        private static Dictionary<string, string> ReadMap(JsonElement map)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in map.EnumerateObject()) result[entry.Name] = ScalarText(entry.Value);
            return result;
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
