using MongoDB.Bson.Serialization.Attributes;

namespace Functions.DomainService.Events
{
    /// <summary>
    /// The event settings of one function: the action names that start it (the Action trigger). Logic-owned
    /// (the function entities live in Genesis and are not changed for this): one row for the draft the editor changes
    /// (<see cref="VersionId"/> null) and one per deployed version, copied from the draft at deploy so a version's list
    /// never changes at run time. Old rows may still hold an <c>Events</c> list; it is ignored and never read.
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

        /// <summary>The action names that start this function. Read by the Action trigger.</summary>
        public List<string> TriggerEvents { get; set; } = [];

        /// <summary>
        /// The Actions switch of the function's Trigger tab. Null (rows written before the switch existed) means on.
        /// Off keeps <see cref="TriggerEvents"/> but no action starts the function.
        /// </summary>
        public bool? ActionsEnabled { get; set; }

        /// <summary>
        /// The one action that starts this function: the first stored name, when the Actions switch is on. Older rows may
        /// hold several names; only the first counts (one action per function, 2026-11-05).
        /// </summary>
        [BsonIgnore]
        public string? ActiveTrigger => ActionsEnabled != false && TriggerEvents.Count > 0 ? TriggerEvents[0] : null;

        public DateTime CreatedDate { get; set; }

        public DateTime LastUpdatedDate { get; set; }

        public string? LastUpdatedBy { get; set; }

        public static string IdFor(string functionId, string? versionId) => $"{functionId}:{versionId ?? "draft"}";
    }
}
