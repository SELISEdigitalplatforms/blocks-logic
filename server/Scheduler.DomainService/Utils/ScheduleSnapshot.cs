using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis.Entities.Logic;

namespace Scheduler.DomainService.Utils
{
    /// <summary>
    /// The schedule as it was at fire time, for <see cref="ScheduleExecution.Snapshot"/>: compact camelCase JSON
    /// (enums as numbers) with the signing secret and every custom header value replaced by
    /// <see cref="Redacted"/>. Header names, the full URL and the payload are kept; the details page already
    /// shows them to anyone who can read the schedule.
    /// </summary>
    public static class ScheduleSnapshot
    {
        public const string Redacted = "[redacted]";

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static string Build(Schedule schedule)
        {
            var node = JsonSerializer.SerializeToNode(schedule, Options)
                       ?? throw new InvalidOperationException("Schedule serialized to null.");

            if (node["webhook"] is JsonObject webhook)
            {
                if (webhook["signingSecret"] is JsonValue secret
                    && secret.TryGetValue<string>(out var value)
                    && !string.IsNullOrEmpty(value))
                {
                    webhook["signingSecret"] = Redacted;
                }

                if (webhook["headers"] is JsonObject headers)
                {
                    foreach (var name in headers.Select(h => h.Key).ToList())
                    {
                        headers[name] = Redacted;
                    }
                }
            }

            return node.ToJsonString();
        }
    }
}
