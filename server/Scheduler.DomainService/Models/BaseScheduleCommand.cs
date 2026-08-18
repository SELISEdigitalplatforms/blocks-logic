using Blocks.Genesis;
using Scheduler.DomainService.Entities;
using System.Text.Json;

namespace Scheduler.DomainService.Models
{
    public class BaseScheduleCommand : BaseEntity
    {
        protected TOutput Prepare<TOutput>(Schedule schedule)
        {
            var scheduleCommand = new BaseScheduleCommand
            {
                ItemId = schedule.ItemId,
                CreatedBy = schedule.CreatedBy,
                Language = schedule.Language,
                CreatedDate = schedule.CreatedDate,
                LastUpdatedBy = schedule.LastUpdatedBy,
                Tags = schedule.Tags
            };
            var serializedParent = JsonSerializer.Serialize(scheduleCommand);
            var castedCommand = JsonSerializer.Deserialize<TOutput>(serializedParent);

            return castedCommand;
        }
    }
}
