using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace DomainService.Workflow.Utils
{
    public static class BsonJsonConverter
    {
        private static readonly JsonWriterSettings RelaxedExtendedJsonSettings = new()
        {
            OutputMode = JsonOutputMode.RelaxedExtendedJson
        };

        public static JsonElement ToJsonElement(BsonValue bson)
        {
            var json = bson.ToJson(RelaxedExtendedJsonSettings);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        public static JsonElement? ToJsonElementOrNull(BsonValue? bson)
        {
            if (bson is null || bson.IsBsonNull)
                return null;

            var json = bson.ToJson(RelaxedExtendedJsonSettings);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        public static BsonValue ToBsonValue(JsonElement element)
        {
            var json = element.GetRawText();
            return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonValue>(json);
        }

        public static BsonArray? ToBsonArrayOrNull(JsonElement? element)
        {
            if (element is null)
                return null;

            var kind = element.Value.ValueKind;
            if (kind == JsonValueKind.Null || kind == JsonValueKind.Undefined)
                return null;

            var bson = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonValue>(element.Value.GetRawText());
            return bson as BsonArray;
        }
    }
}