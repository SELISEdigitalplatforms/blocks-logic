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
    }
}
