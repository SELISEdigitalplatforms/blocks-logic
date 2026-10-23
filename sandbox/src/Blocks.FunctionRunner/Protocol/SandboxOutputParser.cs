using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blocks.FunctionRunner.Contracts;

namespace Blocks.FunctionRunner.Protocol
{
    /// <summary>What the runner made of a sandbox's stdout.</summary>
    public sealed class SandboxOutput
    {
        /// <summary>Log lines, already capped, as the NDJSON that will be stored in Redis.</summary>
        public List<string> Logs { get; } = [];

        /// <summary>True when either the sandbox or this parser cut the logs short.</summary>
        public bool Truncated { get; internal set; }

        /// <summary>Why the logs were cut short: <c>log_bytes</c>, <c>log_lines</c>, or null.</summary>
        public string? TruncationReason { get; internal set; }

        /// <summary>The serialized result value, when the run succeeded.</summary>
        public string? ResultJson { get; internal set; }

        /// <summary>
        /// The raw <c>events</c> array of the success line, exactly as the sandbox wrote it and NOT yet
        /// trusted: the sandbox is untrusted, so <see cref="SandboxOutputParser.ValidateEvents"/> must
        /// accept it before anything is stored or sent. Null when the line had none.
        /// </summary>
        public string? EventsJson { get; internal set; }

        /// <summary>True when a result line said ok, false when it said not-ok, null when absent.</summary>
        public bool? Ok { get; internal set; }

        /// <summary>
        /// When the handler began, as the sandbox's own clock reported it. Null for a sandbox that
        /// never got that far — it failed during boot or import — and for an older runtime image
        /// that does not emit the line.
        /// </summary>
        public long? StartedAtUnixMs { get; internal set; }

        public string? ErrorCode { get; internal set; }
        public string? ErrorMessage { get; internal set; }
        public string? ErrorStack { get; internal set; }

        /// <summary>Lines that were not valid protocol JSON, kept for diagnosis (capped).</summary>
        public List<string> Malformed { get; } = [];

        public long LogBytes { get; internal set; }
    }

    /// <summary>What <see cref="SandboxOutputParser.ValidateEvents"/> made of a run's events.</summary>
    /// <param name="Json">The accepted events as one JSON array; null when there are none or they were rejected.</param>
    /// <param name="Count">How many events <paramref name="Json"/> holds.</param>
    /// <param name="Rejected"><c>EVENTS_REJECTED: &lt;reason&gt;</c> when all events were dropped; the reason never quotes a payload.</param>
    public sealed record EventsCheck(string? Json, int Count, string? Rejected)
    {
        public static EventsCheck None { get; } = new(null, 0, null);
    }

    /// <summary>
    /// Parses the sandbox stdout protocol and enforces the log and result ceilings a second
    /// time, out of process.
    /// <para>
    /// The trusted bootstrap already caps its own output, but it shares a process with tenant
    /// code and is hardened rather than isolated. This parser assumes nothing about it: it
    /// counts bytes and lines itself and stops accepting either way. A sandbox that floods
    /// stdout therefore costs the runner a bounded amount of memory, whatever it emits.
    /// </para>
    /// </summary>
    public sealed class SandboxOutputParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private const int MaxMalformedKept = 20;
        private const int MaxSingleLineBytes = 64 * 1024;

        /// <summary>
        /// A <c>result</c> line is the one line allowed to be long: it carries the value (up to the
        /// result ceiling) and the events (up to their total), plus framing and each event's id/type.
        /// </summary>
        private const string ResultLinePrefix = "{\"t\":\"result\"";
        private const int ResultLineFraming = 64 * 1024;

        /// <summary><c>FunctionEvent</c> names; the same rule as the sandbox's and the Worker's.</summary>
        private static readonly Regex FunctionEventName = new(
            @"^[a-z][a-z0-9_.-]{2,63}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

        private const int MaxEventIdChars = 256;

        private readonly long _logByteCeiling;
        private readonly int _logLineCeiling;
        private readonly long _resultByteCeiling;

        public SandboxOutputParser(
            long logByteCeiling = Ceilings.LogBytes,
            int logLineCeiling = Ceilings.LogLines,
            long resultByteCeiling = Ceilings.ResultBytes)
        {
            _logByteCeiling = logByteCeiling;
            _logLineCeiling = logLineCeiling;
            _resultByteCeiling = resultByteCeiling;
        }

        /// <summary>
        /// Parses a whole stdout capture. Lines are newline-delimited JSON; anything else is
        /// recorded as malformed rather than discarded silently, because npm packages sometimes
        /// write straight to fd 1 and that is worth seeing when a run misbehaves.
        /// </summary>
        public SandboxOutput Parse(string stdout)
        {
            var output = new SandboxOutput();
            if (string.IsNullOrEmpty(stdout)) return output;

            foreach (var rawLine in stdout.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0) continue;
                Consume(output, line);
            }

            return output;
        }

        /// <summary>
        /// Folds one already-split line into <paramref name="output"/>, under the same ceilings as
        /// <see cref="Parse"/>. A reusable sandbox's stdout never ends between calls, so its lines
        /// are routed one at a time — each call's own lines into that call's output.
        /// </summary>
        public void Feed(SandboxOutput output, string line)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (string.IsNullOrEmpty(line)) return;
            line = line.TrimEnd('\r');
            if (line.Length == 0) return;
            Consume(output, line);
        }

        private void Consume(SandboxOutput output, string line)
        {
            // A single absurd line is clipped before anything tries to parse it. A result line is
            // exempt up to what it may legitimately hold; its value and events are checked below.
            var maxBytes = line.StartsWith(ResultLinePrefix, StringComparison.Ordinal)
                ? _resultByteCeiling + Ceilings.EventsLineBytes + ResultLineFraming
                : MaxSingleLineBytes;
            if (Encoding.UTF8.GetByteCount(line) > maxBytes)
            {
                line = line[..Math.Min(line.Length, MaxSingleLineBytes / 4)];
            }

            SandboxEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<SandboxEvent>(line, JsonOptions);
            }
            catch (JsonException)
            {
                RecordMalformed(output, line);
                return;
            }

            if (evt?.Type is null)
            {
                RecordMalformed(output, line);
                return;
            }

            switch (evt.Type)
            {
                case "log":
                    AppendLog(output, line);
                    break;

                case "started":
                    // The moment the tenant's own code began. Everything before it is boot and
                    // module import, which is the platform's time, not theirs. Recorded so the two
                    // can be reported apart and so the kill timer measures the right thing.
                    output.StartedAtUnixMs ??= evt.At;
                    break;

                case "truncated":
                    output.Truncated = true;
                    output.TruncationReason ??= evt.Reason ?? "unspecified";
                    break;

                case "chunk":
                    // A piece of a streamed answer (F-5). Relayed live by a warm call; a single
                    // run is read whole, and its result already holds the text.
                    break;

                case "result":
                    // Only the first result line counts. A function that manages to emit a
                    // second one cannot overwrite the verdict.
                    if (output.Ok is not null) return;
                    ReadResult(output, evt, line);
                    break;

                default:
                    RecordMalformed(output, line);
                    break;
            }
        }

        private void AppendLog(SandboxOutput output, string line)
        {
            if (output.Logs.Count >= _logLineCeiling)
            {
                MarkTruncated(output, "log_lines");
                return;
            }

            var size = Encoding.UTF8.GetByteCount(line) + 1;
            if (output.LogBytes + size > _logByteCeiling)
            {
                MarkTruncated(output, "log_bytes");
                return;
            }

            output.Logs.Add(line);
            output.LogBytes += size;
        }

        private static void MarkTruncated(SandboxOutput output, string reason)
        {
            output.Truncated = true;
            output.TruncationReason ??= reason;
        }

        private void ReadResult(SandboxOutput output, SandboxEvent evt, string line)
        {
            if (evt.Ok == true)
            {
                // Re-extract the raw `value` rather than re-serializing the deserialized object:
                // round-tripping through object would lose numeric precision and property order.
                var (value, events) = ExtractRaw(line);
                if (value is null)
                {
                    output.Ok = false;
                    output.ErrorCode = ErrorCodes.ResultNotSerializable;
                    output.ErrorMessage = "the sandbox reported success but its result value could not be read";
                    return;
                }

                if (Encoding.UTF8.GetByteCount(value) > _resultByteCeiling)
                {
                    output.Ok = false;
                    output.ErrorCode = ErrorCodes.ResultTooLarge;
                    output.ErrorMessage = $"the result exceeds the {_resultByteCeiling} byte ceiling";
                    return;
                }

                output.Ok = true;
                output.ResultJson = value;
                output.EventsJson = events;
                return;
            }

            output.Ok = false;
            output.ErrorCode = string.IsNullOrWhiteSpace(evt.Code) ? ErrorCodes.UserRuntimeError : evt.Code;
            output.ErrorMessage = evt.ErrorMessage;
            output.ErrorStack = evt.Stack;
        }

        /// <summary>The raw <c>value</c> (or "null" when absent; null for unreadable JSON) and the raw <c>events</c> array, if any.</summary>
        private static (string? Value, string? Events) ExtractRaw(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var value = doc.RootElement.TryGetProperty("value", out var v) ? v.GetRawText() : "null";
                var events = doc.RootElement.TryGetProperty("events", out var e) ? e.GetRawText() : null;
                return (value, events);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Checks a success line's events a second time, out of process. The sandbox is untrusted: its
        /// own checks (<c>ctx.events.add</c>) are for the developer, and tenant code can write a forged
        /// line to fd 1, so nothing here relies on them. Everything must hold or ALL events are dropped
        /// (the result is kept): an array of at most <see cref="Ceilings.EventsPerCall"/> items, each
        /// <c>{id, type, payload}</c> with a string id (unique, starting with <paramref name="idPrefix"/>
        /// when given), a type from <paramref name="allowedTypes"/> that the platform knows, an object
        /// payload of at most <see cref="Ceilings.EventPayloadBytes"/> bytes, at most
        /// <see cref="Ceilings.EventsTotalBytes"/> in all, and a valid name on a <c>FunctionEvent</c>.
        /// The reason names an index and a rule, never a payload.
        /// </summary>
        /// <param name="rawEvents"><see cref="SandboxOutput.EventsJson"/>.</param>
        /// <param name="allowedTypes">The types this function may send, from its envelope.</param>
        /// <param name="idPrefix">Event ids must start with this (<c>{runId}-{attempt}-</c>), so a forged id cannot collide with another run's.</param>
        public static EventsCheck ValidateEvents(string? rawEvents, IReadOnlyCollection<string> allowedTypes, string? idPrefix = null)
        {
            if (rawEvents is null) return EventsCheck.None;
            ArgumentNullException.ThrowIfNull(allowedTypes);

            try
            {
                using var doc = JsonDocument.Parse(rawEvents);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return Reject("the events are not a list");

                var count = root.GetArrayLength();
                if (count == 0) return EventsCheck.None;
                if (count > Ceilings.EventsPerCall) return Reject($"{count} events; the limit is {Ceilings.EventsPerCall}");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                long total = 0;
                var index = 0;
                foreach (var item in root.EnumerateArray())
                {
                    index++;
                    var problem = CheckEvent(item, allowedTypes, idPrefix, ids, out var payloadBytes);
                    if (problem is not null) return Reject($"event {index}: {problem}");

                    total += payloadBytes;
                    if (total > Ceilings.EventsTotalBytes)
                    {
                        return Reject($"the payloads together exceed {Ceilings.EventsTotalBytes} bytes");
                    }
                }

                return new EventsCheck(root.GetRawText(), count, null);
            }
            catch (JsonException)
            {
                return Reject("the events are not valid JSON");
            }
        }

        private static EventsCheck Reject(string reason) => new(null, 0, $"{ErrorCodes.EventsRejected}: {reason}");

        /// <summary>Why <paramref name="item"/> is not an acceptable event, or null when it is.</summary>
        private static string? CheckEvent(
            JsonElement item, IReadOnlyCollection<string> allowedTypes, string? idPrefix, HashSet<string> ids, out int payloadBytes)
        {
            payloadBytes = 0;
            if (item.ValueKind != JsonValueKind.Object) return "is not an object";

            if (!item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String) return "has no string id";
            var id = idElement.GetString();
            if (string.IsNullOrEmpty(id) || id.Length > MaxEventIdChars) return "has an empty or too long id";
            if (idPrefix is not null && !id.StartsWith(idPrefix, StringComparison.Ordinal)) return "has an id that does not belong to this run";
            if (!ids.Add(id)) return "repeats an id";

            if (!item.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) return "has no string type";
            var type = typeElement.GetString();
            if (!EventTypes.IsKnown(type) || !allowedTypes.Contains(type!)) return "has a type that is not allowed for this function";

            if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return "has no object payload";
            payloadBytes = Encoding.UTF8.GetByteCount(payload.GetRawText());
            if (payloadBytes > Ceilings.EventPayloadBytes) return $"has a payload over {Ceilings.EventPayloadBytes} bytes";

            if (type == EventTypes.FunctionEvent && !IsValidFunctionEvent(payload)) return "has an invalid FunctionEvent name";
            return null;
        }

        private static bool IsValidFunctionEvent(JsonElement payload)
        {
            if (!payload.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return false;
            var text = name.GetString();
            return text is not null && FunctionEventName.IsMatch(text);
        }

        private static void RecordMalformed(SandboxOutput output, string line)
        {
            if (output.Malformed.Count >= MaxMalformedKept) return;
            output.Malformed.Add(line.Length > 512 ? line[..512] : line);
        }
    }
}
