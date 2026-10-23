using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Functions.DomainService.Events
{
    /// <summary>
    /// The events one function may send. Logic-owned (the function entities live in Genesis and are not changed for
    /// this): one row for the draft the editor changes (<see cref="VersionId"/> null) and one per deployed version,
    /// copied from the draft at deploy so a version's list never changes at run time.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class FunctionEventSettingsEntity
    {
        /// <summary>Always <see cref="IdFor"/>, so a function has at most one draft row and one row per version.</summary>
        [BsonId]
        public string ItemId { get; set; } = string.Empty;

        public string FunctionId { get; set; } = string.Empty;

        /// <summary>Null for the draft.</summary>
        public string? VersionId { get; set; }

        /// <summary>The events the function may send, each with the payload template shown in the editor.</summary>
        public List<FunctionEventSetting> Events { get; set; } = [];

        /// <summary>The <c>FunctionEvent</c> names that start this function. Read by the event trigger.</summary>
        public List<string> TriggerEvents { get; set; } = [];

        public DateTime CreatedDate { get; set; }

        public DateTime LastUpdatedDate { get; set; }

        public string? LastUpdatedBy { get; set; }

        public static string IdFor(string functionId, string? versionId) => $"{functionId}:{versionId ?? "draft"}";
    }

    [BsonIgnoreExtraElements]
    public class FunctionEventSetting
    {
        /// <summary>A <see cref="FunctionEventCatalog"/> type.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The payload template as JSON text.</summary>
        public string PayloadTemplateJson { get; set; } = "{}";
    }

    public enum FunctionEventStatus
    {
        Sent,
        Failed,
        Rejected,
    }

    /// <summary>
    /// What happened to the events of one run attempt. One document per attempt; written when the Worker has finished
    /// with them. Holds ids, types and outcomes, never a payload.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class FunctionRunEventsEntity
    {
        /// <summary><c>{runId}:{attempt}</c>.</summary>
        [BsonId]
        public string ItemId { get; set; } = string.Empty;

        public string RunId { get; set; } = string.Empty;

        public string FunctionId { get; set; } = string.Empty;

        public int Attempt { get; set; }

        public List<FunctionRunEventEntry> Events { get; set; } = [];

        /// <summary>The runner's <c>EVENTS_REJECTED: …</c> reason, when it dropped every event of the run.</summary>
        [BsonIgnoreIfNull]
        public string? Rejected { get; set; }

        public DateTime CreatedDate { get; set; }

        public static string IdFor(string runId, int attempt) => $"{runId}:{attempt}";
    }

    [BsonIgnoreExtraElements]
    public class FunctionRunEventEntry
    {
        public string Id { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        /// <summary>The name of a <c>FunctionEvent</c>; empty for other types.</summary>
        public string Name { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public FunctionEventStatus Status { get; set; }

        [BsonIgnoreIfNull]
        public string? Error { get; set; }

        /// <summary>How many sends were tried; 0 when none was (rejected, expired, unknown delivery, rate limited).</summary>
        public int Attempts { get; set; }

        [BsonIgnoreIfNull]
        public DateTime? SentAt { get; set; }
    }
}
