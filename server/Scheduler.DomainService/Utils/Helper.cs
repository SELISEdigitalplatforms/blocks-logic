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

        /// <summary>
        /// Application schedules accept 5-field crons only, so one fires at most once a minute (1,440 history
        /// rows a day). Stored 6-field Application schedules keep firing; only create/update refuse them.
        /// </summary>
        public static bool IsValidApplicationCronExpression(string cronExpression)
        {
            if (string.IsNullOrWhiteSpace(cronExpression))
                return false;

            return CronExpression.TryParse(cronExpression, CronFormat.Standard, out _);
        }
    }
}
