using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Language
{
    [BsonIgnoreExtraElements]
    public class Language
    {
        [BsonId]
        public string? ItemId { get; set; }
        public string LanguageName { get; set; }
        public string LanguageCode { get; set; }
        public bool IsDefault { get; set; } = false;
    }
}
