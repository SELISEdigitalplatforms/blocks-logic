using System.Collections;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Workflow.DomainService.Nodes
{
    /// <summary>
    /// Explains why a node's saved parameters could not be read into its parameter class, in words a
    /// workflow author can act on: which field (by its JSON path) and which shape was expected vs sent.
    /// Used only after the real deserialization has failed, so it adds no cost to a good run.
    /// Never includes a parameter value in the message: values can hold secrets (client secrets,
    /// tokens, bodies). Field names and dictionary keys are names, not values, and are shown.
    /// </summary>
    public static class NodeParameterShape
    {
        private static readonly DefaultContractResolver Resolver = new();

        /// <summary>
        /// Walks <paramref name="parameters"/> against <paramref name="targetType"/> the way Newtonsoft
        /// would bind it and returns a message for the first field whose JSON shape cannot fit the
        /// declared type. Returns null when no shape mismatch is found (or the JSON cannot be parsed).
        /// </summary>
        public static string? Describe(string parametersJson, Type targetType)
        {
            JToken token;
            try
            {
                token = JToken.Parse(parametersJson);
            }
            catch
            {
                return null;
            }

            return FindMismatch(token, targetType, path: string.Empty, depth: 0);
        }

        /// <summary>
        /// Message for when <see cref="Describe"/> finds nothing but the read still failed. Shows the
        /// field path from the serializer (names only), never the serializer's own message: Newtonsoft
        /// quotes the offending value (e.g. "Error converting value \"...\"").
        /// </summary>
        public static string Fallback(string? path) =>
            string.IsNullOrEmpty(path)
                ? "The node parameters have the wrong shape and could not be read."
                : $"Parameter '{path}' has the wrong shape and could not be read.";

        private static string? FindMismatch(JToken token, Type type, string path, int depth)
        {
            // Saved parameters are shallow; the cap only stops a hostile, very deep document.
            if (depth > 32 || token.Type is JTokenType.Null or JTokenType.Undefined)
            {
                return null; // nulls are ignored on read (NullValueHandling.Ignore)
            }

            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(object) || typeof(JToken).IsAssignableFrom(type))
            {
                return null;
            }

            JsonContract contract;
            try
            {
                contract = Resolver.ResolveContract(type);
            }
            catch
            {
                return null;
            }

            if (contract.Converter != null)
            {
                return null; // a custom converter decides its own shape
            }

            switch (contract)
            {
                case JsonPrimitiveContract:
                    if (token is JContainer) return Mismatch(path, ExpectedPrimitive(type), token);
                    // Newtonsoft accepts "5" for a number and "true" for a bool; other text fails.
                    if (token.Type == JTokenType.String && IsNumber(type)
                        && !double.TryParse((string?)token, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                        return Mismatch(path, "a number", token);
                    if (token.Type == JTokenType.String && type == typeof(bool) && !bool.TryParse((string?)token, out _))
                        return Mismatch(path, "true or false", token);
                    return null;

                case JsonDictionaryContract dictionary:
                    if (token is not JObject dictObject)
                    {
                        return Mismatch(path, ExpectedDictionary(dictionary.DictionaryValueType), token);
                    }
                    var valueType = dictionary.DictionaryValueType ?? typeof(object);
                    foreach (var property in dictObject.Properties())
                    {
                        var found = FindMismatch(property.Value, valueType, Join(path, property.Name), depth + 1);
                        if (found != null) return found;
                    }
                    return null;

                case JsonArrayContract array:
                    if (token is not JArray list)
                    {
                        return Mismatch(path, ExpectedList(array.CollectionItemType), token);
                    }
                    var itemType = array.CollectionItemType ?? typeof(object);
                    for (var i = 0; i < list.Count; i++)
                    {
                        var found = FindMismatch(list[i], itemType, $"{path}[{i}]", depth + 1);
                        if (found != null) return found;
                    }
                    return null;

                case JsonObjectContract objectContract:
                    if (token is not JObject jsonObject)
                    {
                        return Mismatch(path, "an object", token);
                    }
                    foreach (var property in jsonObject.Properties())
                    {
                        // Same lookup Newtonsoft uses: exact name first, then case-insensitive.
                        var member = objectContract.Properties.GetClosestMatchProperty(property.Name);
                        if (member == null || member.Ignored || !member.Writable || member.PropertyType == null)
                        {
                            continue; // unknown fields are ignored on read
                        }
                        if (member.Converter != null)
                        {
                            continue;
                        }
                        var found = FindMismatch(property.Value, member.PropertyType, Join(path, property.Name), depth + 1);
                        if (found != null) return found;
                    }
                    return null;

                default:
                    return null;
            }
        }

        private static string Join(string path, string name) =>
            string.IsNullOrEmpty(path) ? name : $"{path}.{name}";

        private static string Mismatch(string path, string expected, JToken actual) =>
            string.IsNullOrEmpty(path)
                ? $"The node parameters have the wrong shape: expected {expected}, got {Kind(actual)}."
                : $"Parameter '{path}' has the wrong shape: expected {expected}, got {Kind(actual)}.";

        private static string Kind(JToken token) => token.Type switch
        {
            JTokenType.Object => "an object",
            JTokenType.Array => "an array",
            JTokenType.String => "text",
            JTokenType.Integer or JTokenType.Float => "a number",
            JTokenType.Boolean => "true or false",
            JTokenType.Date => "a date",
            _ => "a value of another kind",
        };

        private static string ExpectedPrimitive(Type type)
        {
            if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)) return "text";
            if (type == typeof(bool)) return "true or false";
            if (type.IsEnum) return "text";
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "a date";
            return IsNumber(type) ? "a number" : "a single value";
        }

        private static string ExpectedDictionary(Type? valueType)
        {
            if (valueType == null || valueType == typeof(object)) return "an object";
            var inner = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (inner == typeof(string)) return "an object of text values";
            if (inner == typeof(bool)) return "an object of true/false values";
            if (IsNumber(inner)) return "an object of number values";
            return "an object";
        }

        private static string ExpectedList(Type? itemType)
        {
            if (itemType == null || itemType == typeof(object)) return "an array";
            var inner = Nullable.GetUnderlyingType(itemType) ?? itemType;
            if (inner == typeof(string)) return "an array of text values";
            if (IsNumber(inner)) return "an array of numbers";
            if (inner == typeof(bool)) return "an array of true/false values";
            return typeof(IEnumerable).IsAssignableFrom(inner) || inner.IsPrimitive ? "an array" : "an array of objects";
        }

        private static bool IsNumber(Type type) =>
            type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte)
            || type == typeof(double) || type == typeof(float) || type == typeof(decimal);
    }
}
