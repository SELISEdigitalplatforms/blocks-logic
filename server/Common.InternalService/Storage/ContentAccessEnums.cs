using System.Text.Json.Serialization;

namespace Common.InternalService.Storage
{
    /// <summary>
    /// JSON-bound as a string ("Directory"/"File") on the API surface via
    /// <see cref="JsonStringEnumConverter"/>, and stored as a string in Mongo via
    /// BsonRepresentation(BsonType.String) on the entities that use it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContentResourceType
    {
        Directory = 1,
        File = 2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContentPrincipalType
    {
        User = 1,
        Role = 2,
        Everyone = 3,
        Organization = 4
    }

    /// <summary>
    /// Ordered from least to most capable. A higher permission satisfies every lower
    /// operation: Owner covers Manage, which covers Delete, Edit, Download and View.
    /// The resolver relies on the numeric order, so do not renumber these members.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContentPermission
    {
        View = 1,
        Download = 2,
        Edit = 3,
        Delete = 4,
        Manage = 5,
        Owner = 6
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContentEffect
    {
        Allow = 1,
        Deny = 2
    }
}
