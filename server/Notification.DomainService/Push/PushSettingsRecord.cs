using Blocks.Genesis.Entities.Logic;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Notification.Push
{
    /// <summary>
    /// The tenant's KV <c>firebase-config</c>. blocks-os writes it; logic only reads it. Same field names as genesis
    /// <see cref="FirebasePushConfiguration"/>. Never a key or a web config value: those live in Blocks Secrets.
    /// <see cref="ProjectId"/> is for display only: sending takes the project from the key itself.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class PushSettingsRecord
    {
        public const string DefaultWebSecretName = "firebase-web-config";

        public string ProjectId { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public string SecretName { get; set; } = FirebasePushConfiguration.DefaultSecretName;

        [BsonIgnoreIfNull]
        public string? SecretId { get; set; }

        [BsonIgnoreIfNull]
        public DateTime? KeyUploadedAt { get; set; }

        [BsonIgnoreIfNull]
        public string? KeyUploadedBy { get; set; }

        /// <summary>A record without this field reads as Android and iOS on, Web off.</summary>
        public PushPlatformSwitches? Platforms { get; set; } = new();

        public string WebSecretName { get; set; } = DefaultWebSecretName;

        [BsonIgnoreIfNull]
        public string? WebSecretId { get; set; }

        [BsonIgnoreIfNull]
        public DateTime? WebConfigUploadedAt { get; set; }

        [BsonIgnoreIfNull]
        public string? WebConfigUploadedBy { get; set; }

        public bool HasKey => KeyUploadedAt is not null || !string.IsNullOrWhiteSpace(SecretId);

        public bool HasWebConfig => WebConfigUploadedAt is not null || !string.IsNullOrWhiteSpace(WebSecretId);

        /// <summary>Master switch on and a key stored. The KV project id is not needed: the key names its project.</summary>
        public bool CanSend => Enabled && HasKey;

        /// <summary>The platform's switch (an unknown platform is off). Web also needs a stored web config.</summary>
        public bool IsOn(string platform)
        {
            var switches = Platforms ?? new PushPlatformSwitches();
            return platform switch
            {
                PushPlatforms.Android => switches.Android,
                PushPlatforms.Ios => switches.Ios,
                PushPlatforms.Web => switches.Web && HasWebConfig,
                _ => false,
            };
        }
    }

    /// <summary>Per-platform on/off of <see cref="PushSettingsRecord"/>. No BsonIgnoreIfDefault: false must be stored.</summary>
    [BsonIgnoreExtraElements]
    public sealed class PushPlatformSwitches
    {
        public bool Android { get; set; } = true;
        public bool Ios { get; set; } = true;
        public bool Web { get; set; }
    }

    /// <summary>The platform names devices and requests use.</summary>
    public static class PushPlatforms
    {
        public const string Android = "android";
        public const string Ios = "ios";
        public const string Web = "web";

        public static IReadOnlyList<string> All { get; } = [Android, Ios, Web];

        /// <summary>The known platform name of <paramref name="value"/> (trimmed, lower case), else null.</summary>
        public static string? Normalize(string? value)
        {
            var name = value?.Trim().ToLowerInvariant();
            return name is not null && All.Contains(name) ? name : null;
        }
    }
}
