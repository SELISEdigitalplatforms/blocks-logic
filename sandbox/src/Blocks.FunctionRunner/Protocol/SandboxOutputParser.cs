using System.Text;
using System.Text.Json;
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

        /// <summary>True when a result line said ok, false when it said not-ok, null when absent.</summary>
        public bool? Ok { get; internal set; }

        public string? ErrorCode { get; internal set; }
        public string? ErrorMessage { get; internal set; }
        public string? ErrorStack { get; internal set; }

        /// <summary>Lines that were not valid protocol JSON, kept for diagnosis (capped).</summary>
        public List<string> Malformed { get; } = [];

        public long LogBytes { get; internal set; }
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

        private void Consume(SandboxOutput output, string line)
        {
            // A single absurd line is clipped before anything tries to parse it.
            if (Encoding.UTF8.GetByteCount(line) > MaxSingleLineBytes)
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

                case "truncated":
                    output.Truncated = true;
                    output.TruncationReason ??= evt.Reason ?? "unspecified";
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
                var value = ExtractRawValue(line);
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
                return;
            }

            output.Ok = false;
            output.ErrorCode = string.IsNullOrWhiteSpace(evt.Code) ? ErrorCodes.UserRuntimeError : evt.Code;
            output.ErrorMessage = evt.ErrorMessage;
            output.ErrorStack = evt.Stack;
        }

        private static string? ExtractRawValue(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.TryGetProperty("value", out var value)
                    ? value.GetRawText()
                    : "null";
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void RecordMalformed(SandboxOutput output, string line)
        {
            if (output.Malformed.Count >= MaxMalformedKept) return;
            output.Malformed.Add(line.Length > 512 ? line[..512] : line);
        }
    }
}
