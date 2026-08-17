using Cronos;

namespace Scheduler.DomainService.Utils
{
    public static class Helper
    {
        public static bool IsValidCronExpression(string cronExpression)
        {
            if (string.IsNullOrWhiteSpace(cronExpression))
                return false;

            if (!CronExpression.TryParse(cronExpression, CronFormat.Standard, out var cron))
                return false;

            return true;
        }
    }
}
