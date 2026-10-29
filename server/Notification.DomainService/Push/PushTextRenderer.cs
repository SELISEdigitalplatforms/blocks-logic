using System.Text;
using System.Text.Json;

namespace DomainService.Notification.Push
{
    /// <summary>Turns Localization texts with <c>{{field}}</c> placeholders into push titles and bodies.</summary>
    public static class PushTextRenderer
    {
        public const int MaxValueLength = 100;
        public const int MaxTitleLength = 65;
        public const int MaxBodyLength = 240;
        public const int MaxLinkLength = 2048;

        /// <summary>
        /// The payload's top-level string, number and true/false fields, each cut to <see cref="MaxValueLength"/>.
        /// Objects, arrays and nulls are left out (their placeholder renders empty). Not JSON: no fields.
        /// </summary>
        public static IReadOnlyDictionary<string, string> FieldsOf(string? payloadJson)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return fields;

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return fields;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (ValueOf(property.Value) is { } value)
                        fields[property.Name] = Cut(value, MaxValueLength);
                }
            }
            catch (JsonException)
            {
                // A payload that is not JSON fills no placeholder; the push still goes with the plain text.
                fields.Clear();
            }

            return fields;
        }

        /// <summary>
        /// Replaces each <c>{{name}}</c> (and, with <paramref name="singleBraces"/>, each <c>{name}</c>) with the field's
        /// value (missing: empty), then cuts the result to <paramref name="maxLength"/>. With
        /// <paramref name="escapeValues"/> values are URL-escaped (links).
        /// </summary>
        public static string Fill(string template, IReadOnlyDictionary<string, string> fields, int maxLength, bool escapeValues = false,
            bool singleBraces = false)
        {
            var result = new StringBuilder(template.Length);
            var position = 0;
            foreach (var placeholder in Placeholders(template, singleBraces))
            {
                result.Append(template, position, placeholder.Start - position);
                var value = fields.TryGetValue(placeholder.Name, out var found) ? found : string.Empty;
                result.Append(escapeValues ? Uri.EscapeDataString(value) : value);
                position = placeholder.End;
            }

            result.Append(template, position, template.Length - position);
            return Cut(result.ToString(), maxLength);
        }

        /// <summary>Sample data of a draft test: every placeholder of the texts filled with its own field name.</summary>
        public static IReadOnlyDictionary<string, string> SampleFields(IEnumerable<string?> templates)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var template in templates.OfType<string>())
            {
                foreach (var name in Placeholders(template, singleBraces: true).Select(p => p.Name))
                    fields[name] = Cut(name, MaxValueLength);
            }

            return fields;
        }

        /// <summary><c>{{name}}</c> placeholders in order; with <paramref name="singleBraces"/> also <c>{name}</c>.</summary>
        private static IEnumerable<Placeholder> Placeholders(string template, bool singleBraces)
        {
            var position = 0;
            while (position < template.Length)
            {
                var open = template.IndexOf('{', position);
                if (open < 0)
                    yield break;

                var isDouble = open + 1 < template.Length && template[open + 1] == '{';
                if (!isDouble && !singleBraces)
                {
                    position = open + 1;
                    continue;
                }

                var closeMark = isDouble ? "}}" : "}";
                var nameStart = open + (isDouble ? 2 : 1);
                var close = template.IndexOf(closeMark, nameStart, StringComparison.Ordinal);
                if (close < 0)
                    yield break;

                yield return new Placeholder(open, close + closeMark.Length, template[nameStart..close].Trim());
                position = close + closeMark.Length;
            }
        }

        private readonly record struct Placeholder(int Start, int End, string Name);

        /// <summary>The text for <paramref name="language"/>, else the default language, else the key name itself.</summary>
        public static string Pick(IReadOnlyDictionary<string, string>? cultures, string? language, string? defaultLanguage, string keyName)
        {
            return Match(cultures, language) ?? Match(cultures, defaultLanguage) ?? keyName;
        }

        /// <summary>Exact culture first (en-US), then the same language (en-GB for en-US).</summary>
        private static string? Match(IReadOnlyDictionary<string, string>? cultures, string? language)
        {
            if (cultures is null || string.IsNullOrWhiteSpace(language))
                return null;

            if (cultures.TryGetValue(language, out var exact))
                return exact;

            var primary = Primary(language);
            return cultures.FirstOrDefault(c => string.Equals(Primary(c.Key), primary, StringComparison.OrdinalIgnoreCase)).Value;
        }

        private static string Primary(string culture)
        {
            var dash = culture.IndexOf('-');
            return dash < 0 ? culture : culture[..dash];
        }

        private static string? ValueOf(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

        /// <summary>Cuts to <paramref name="maxLength"/> characters without splitting a surrogate pair.</summary>
        internal static string Cut(string text, int maxLength)
        {
            if (text.Length <= maxLength)
                return text;

            var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
            return text[..length];
        }
    }
}
