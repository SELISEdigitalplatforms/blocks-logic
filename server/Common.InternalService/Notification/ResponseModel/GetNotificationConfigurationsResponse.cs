using Blocks.Genesis;
using Common.InternalService.Notification.Entities;

namespace Common.InternalService.Notification.ResponseModel
{
    public class GetNotificationConfigurationsResponse : BaseResponse
    {
        public long TotalCount { get; set; }
        public List<NotificationConfiguration> Configurations { get; set; }
    }
}
