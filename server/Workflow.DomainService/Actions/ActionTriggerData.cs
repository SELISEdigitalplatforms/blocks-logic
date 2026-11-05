using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis;
using MongoDB.Bson;

namespace Workflow.DomainService.Actions
{
    /// <summary>
    /// The one simple JSON a workflow and a function receive for an action (the same body a webhook gets):
    /// <c>{ "id", "action", "time", "organizationId", "data" }</c>. No tenant, user or trace id: the item already belongs
    /// to the tenant, and those travel in the message context.
    /// </summary>
    public static class ActionTriggerData
    {
        /// <summary>The field that holds the event id; the workflow dedupe reads it from the stored trigger data.</summary>
        public const string IdField = "id";

        /// <summary>The action as one BSON document. Data that is not a JSON object becomes an empty object.</summary>
        public static BsonDocument ToDocument(BlocksAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return new BsonDocument
            {
                { IdField, action.Id ?? string.Empty },
                { "action", action.Action ?? string.Empty },
                { "time", FormatTime(action.Time) },
                { "organizationId", string.IsNullOrEmpty(action.OrganizationId) ? BsonNull.Value : new BsonString(action.OrganizationId) },
                { "data", ParseData(action.DataJson) },
            };
        }

        /// <summary>The workflow trigger node's one output item.</summary>
        public static BsonArray ToTriggerItems(BlocksAction action) => [ToDocument(action)];

        /// <summary>The function's <c>input</c> as JSON text; <c>data</c> keeps the sent JSON exactly (numbers included).</summary>
        public static string ToJson(BlocksAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            var body = new JsonObject
            {
                [IdField] = action.Id ?? string.Empty,
                ["action"] = action.Action ?? string.Empty,
                ["time"] = FormatTime(action.Time),
                ["organizationId"] = string.IsNullOrEmpty(action.OrganizationId) ? null : action.OrganizationId,
                ["data"] = ParseJsonObject(action.DataJson),
            };
            return body.ToJsonString();
        }

        private static string FormatTime(DateTime time) =>
            DateTime.SpecifyKind(time, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        private static JsonObject ParseJsonObject(string? dataJson)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return [];
            try
            {
                return JsonNode.Parse(dataJson) as JsonObject ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        // Through System.Text.Json first, so anything that is not a valid JSON object becomes {} the same way for both forms.
        private static BsonDocument ParseData(string? dataJson) => BsonDocument.Parse(ParseJsonObject(dataJson).ToJsonString());
    }
}
