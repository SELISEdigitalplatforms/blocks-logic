using Cronos;

namespace Scheduler.DomainService.Utils
{
    public static class Helper
    {
        public static bool IsValidCronExpression(string cronExpression)
        {
            if (string.IsNullOrWhiteSpace(cronExpression))
                return false;

            // Accept both 5-field (standard) and 6-field (with leading seconds)
            // expressions; the workflow schedule node generates 6-field crons.
            return CronExpression.TryParse(cronExpression, CronFormat.Standard, out _) ||
                   CronExpression.TryParse(cronExpression, CronFormat.IncludeSeconds, out _);
        }
    }
}
