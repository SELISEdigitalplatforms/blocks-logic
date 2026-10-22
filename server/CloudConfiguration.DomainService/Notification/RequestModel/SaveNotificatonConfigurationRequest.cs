using Blocks.Genesis;
using Blocks.Genesis.Entities.Os;

namespace CloudConfiguration.DomainService.Notification.RequestModel
{
    public class SaveNotificatonConfigurationRequest 
    {
        public string Name { get; set; }
        public NotifierTypes ChannelToNotify { get; set; }
        public NotificationReceiverTypes NotificationType { get; set; }
        public bool EnablePersistence { get; set; }
        public string NotifyMethod { get; set; }

        public bool IsUpdateRequest { get; set; }

        // Optional built-in fields (null = not sent; an older client sends none and behaves as before).
        public bool? IsBuiltIn { get; set; }
        public bool? IsDisabled { get; set; }
        public string? Description { get; set; }
        public NotificationImportance? Importance { get; set; }
        public NotificationKind? Kind { get; set; }
        public string? SourceService { get; set; }
        public string? ConsumerAppRecommendation { get; set; }
        public string? ConsumerAppNote { get; set; }
    }
}
