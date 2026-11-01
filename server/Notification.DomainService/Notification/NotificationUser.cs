using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification
{
    // Local, minimal read model for the shared "Users" collection owned by Iam.DomainService.
    // Deliberately not referencing Iam.DomainService/Iam.Driver to avoid a project dependency;
    // Mongo does not care which class reads the document, only that field names line up.
    // OrganizationIds is the flat list of organizations the user belongs to; Language the user's
    // culture code (for example "en-US"), used for push texts.
    [BsonIgnoreExtraElements]
    public class NotificationUser
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public List<string> OrganizationIds { get; set; } = [];

        [BsonIgnoreIfNull]
        public string? Language { get; set; }
    }
}
