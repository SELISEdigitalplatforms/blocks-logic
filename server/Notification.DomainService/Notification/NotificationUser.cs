using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification
{
    // Local, minimal read model for the shared "Users" collection owned by Iam.DomainService.
    // Deliberately not referencing Iam.DomainService/Iam.Driver to avoid a project dependency;
    // Mongo does not care which class reads the document, only that field names line up.
    // Mirrors the live document shape: OrganizationIds is the flat list of organizations the
    // user belongs to, and Roles maps each organizationId to the role slugs held within it.
    [BsonIgnoreExtraElements]
    public class NotificationUser
    {
        [BsonId]
        public string ItemId { get; set; }
        public List<string> OrganizationIds { get; set; } = [];
        public Dictionary<string, List<string>> Roles { get; set; } = [];
    }
}
