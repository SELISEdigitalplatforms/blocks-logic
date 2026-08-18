using MongoDB.Bson.Serialization.Attributes;

namespace Scheduler.DomainService.Models
{
    [BsonIgnoreExtraElements]
    public class QueueConfiguration
    {
        public string QueueName { get; set; } = string.Empty;
    }
}
