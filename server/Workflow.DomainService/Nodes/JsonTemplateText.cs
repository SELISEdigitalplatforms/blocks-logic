using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Workflow.DomainService.Nodes
{
    /// <summary>
    /// Thrown when a JSON field (Set Field JSON, Function input, HTTP / Proxy body) is not valid JSON
    /// after its <c>{{…}}</c> values were filled in. Nodes turn it into a failed step
    /// (<see cref="NodeExecutionResult.Failed"/>), never into an error item on a successful step.
    /// The message names the field and the JSON error position; it never carries the filled text.
    /// </summary>
    public sealed class InvalidFilledJsonException : Exception
    {
        public InvalidFilledJsonException(string message) : base(message) { }

        /// <summary>The message with the 1-based item number appended, e.g. "… (item 2)".</summary>
        public string ForItem(int index) => $"{Message} (item {index + 1})";
    }

    /// <summary>Text helpers for filling values into JSON and GraphQL templates.</summary>
    public static class JsonTemplateText
    {
        /// <summary>
        /// Appends <paramref name="value"/> escaped for use inside a JSON (or GraphQL) "…" string literal,
        /// without the surrounding quotes. Escapes <c>"</c>, <c>\</c> and control characters; other
        /// characters (unicode included) stay as they are, which is valid JSON.
        /// </summary>
        public static void AppendEscaped(StringBuilder sb, string value)
        {
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
        }

        public static string Escape(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            AppendEscaped(sb, value);
            return sb.ToString();
        }

        /// <summary>A JSON string literal: quoted and escaped.</summary>
        public static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            AppendEscaped(sb, value);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Whether <paramref name="text"/> is one complete JSON value.</summary>
        public static bool IsValidJson(string text) => TryValidate(text, out _);

        /// <summary>
        /// Parses <paramref name="text"/> as JSON. On failure <paramref name="error"/> says what is wrong and
        /// where (line and position, 1-based). The text itself is never echoed.
        /// </summary>
        public static bool TryValidate(string text, out string? error)
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
                error = null;
                return true;
            }
            catch (JsonException ex)
            {
                var line = (ex.LineNumber ?? 0) + 1;
                var position = (ex.BytePositionInLine ?? 0) + 1;
                // STJ's message ends with its own 0-based "LineNumber: … | BytePositionInLine: …"; keep
                // only the first sentence and give the position in 1-based words instead.
                var reason = ex.Message;
                var cut = reason.IndexOf(" Path:", StringComparison.Ordinal);
                if (cut < 0) cut = reason.IndexOf(" LineNumber:", StringComparison.Ordinal);
                if (cut > 0) reason = reason[..cut];
                error = $"{reason.Trim()} (line {line}, position {position})";
                return false;
            }
        }
    }
}
