using MongoDB.Bson.Serialization.Attributes;

namespace MailBoxSyncService.Entities
{
    /// <summary>
    /// Where an inbound configuration's delta sync resumes.
    /// </summary>
    /// <remarks>
    /// One per configuration, in the tenant's database. The cursor is a Graph next or delta link,
    /// and it names the mailbox it was issued for; it is kept beside that mailbox so an edit that
    /// points the configuration at another one starts a fresh round rather than resuming the old
    /// mailbox's.
    /// </remarks>
    [BsonIgnoreExtraElements]
    public class MailBoxSyncCursor
    {
        /// <summary>The <c>MailServerConfiguration</c> item id.</summary>
        [BsonId]
        public string ConfigurationId { get; set; } = string.Empty;

        public string MailboxAddress { get; set; } = string.Empty;

        public string Cursor { get; set; } = string.Empty;

        public DateTime UpdatedAtUtc { get; set; }
    }
}
