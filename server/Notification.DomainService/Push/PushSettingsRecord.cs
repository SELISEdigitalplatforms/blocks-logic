using Blocks.Genesis.Entities.Logic;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// The tenant's KV <c>firebase-config</c> as logic stores it: genesis <see cref="FirebasePushConfiguration"/>'s fields
    /// (same names, so genesis reads it) plus when and by whom the key was uploaded. Never the key itself: that lives
    /// in Blocks Secrets under <see cref="SecretName"/>.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class PushSettingsRecord
    {
        public string ProjectId { get; set; } = string.Empty;

        [BsonIgnoreIfDefault]
        public bool Enabled { get; set; }

        public string SecretName { get; set; } = FirebasePushConfiguration.DefaultSecretName;

        [BsonIgnoreIfNull]
        public DateTime? KeyUploadedAt { get; set; }

        [BsonIgnoreIfNull]
        public string? KeyUploadedBy { get; set; }

        public bool HasKey => KeyUploadedAt is not null;

        public bool CanSend => Enabled && HasKey && !string.IsNullOrWhiteSpace(ProjectId);
    }
}
