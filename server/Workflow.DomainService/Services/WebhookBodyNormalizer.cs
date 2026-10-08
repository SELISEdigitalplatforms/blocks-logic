using System.Text.Json;
using MongoDB.Bson;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// The webhook body is not a JSON object or an array of objects. The API answers 400 with
    /// <see cref="Exception.Message"/>; it never holds the body itself.
    /// </summary>
    public sealed class InvalidWebhookBodyException : Exception
    {
        public InvalidWebhookBodyException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Turns a webhook body into trigger items: an object is one item, an array of objects is one item
    /// per element. Anything else (a primitive, null, an array holding a primitive or an array) is
    /// refused with <see cref="InvalidWebhookBodyException"/> — it used to throw inside BsonDocument.Parse
    /// and surface as a 500.
    /// </summary>
    public static class WebhookBodyNormalizer
    {
        public const string ShapeMessage = "Body must be a JSON object or an array of objects";

        public static BsonArray Normalize(JsonElement input)
        {
            var items = new BsonArray();

            if (input.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var element in input.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidWebhookBodyException($"{ShapeMessage} (element {index} is {Describe(element.ValueKind)}).");
                    }
                    items.Add(ParseObject(element, index));
                    index++;
                }
                return items;
            }

            if (input.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidWebhookBodyException($"{ShapeMessage} (the body is {Describe(input.ValueKind)}).");
            }

            items.Add(ParseObject(input, index: null));
            return items;
        }

        private static BsonDocument ParseObject(JsonElement element, int? index)
        {
            try
            {
                return BsonDocument.Parse(element.GetRawText());
            }
            catch (Exception ex)
            {
                // e.g. a "$date"/"$oid" style key that Mongo's extended JSON reader rejects.
                var where = index is null ? "the body" : $"element {index}";
                throw new InvalidWebhookBodyException($"{ShapeMessage} ({where} could not be read as an object).", ex);
            }
        }

        private static string Describe(JsonValueKind kind) => kind switch
        {
            JsonValueKind.String => "a string",
            JsonValueKind.Number => "a number",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => "an array",
            JsonValueKind.Undefined => "empty",
            _ => kind.ToString().ToLowerInvariant(),
        };
    }
}
