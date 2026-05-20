using System.Text.Json;
using MongoDB.Bson;

namespace DomainService.Workflow.Utils
{
    public static class BsonJsonConverter
    {
        public static JsonElement ToJsonElement(BsonValue bson)
        {
            var json = bson.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
            {
                OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson
            });

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        public static BsonValue ToBsonValue(JsonElement element)
        {
            var json = element.GetRawText();
            return MongoDB.Bson.Serialization.BsonSerializer
                .Deserialize<BsonValue>(json);
        }
    }

}