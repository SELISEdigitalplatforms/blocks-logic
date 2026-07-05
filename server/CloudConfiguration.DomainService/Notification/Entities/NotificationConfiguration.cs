using Blocks.Genesis;
using CloudConfiguration.DomainService.Notification.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace CloudConfiguration.DomainService.Notification.Entities
{
    [BsonIgnoreExtraElements]
    public class NotificationConfiguration : BaseEntity
    {
        public string Name { get; set; }
        public NotifierTypes ChannelToNotify { get; set; }
        public NotificationReceiverTypes NotificationType { get; set; }
        public string NotifyMethod { get; set; }
        public bool EnablePersistence { get; set; }
    }
}
