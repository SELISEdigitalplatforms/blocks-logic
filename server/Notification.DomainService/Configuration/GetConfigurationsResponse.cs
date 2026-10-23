using Blocks.Genesis;
using Blocks.Genesis.Entities.Os;

namespace DomainService.Configuration
{
    public class GetConfigurationsResponse : BaseResponse
    {
        public long TotalCount { get; set; }
        public List<NotificationConfiguration> Configurations { get; set; } 
    }
}
