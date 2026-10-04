namespace Scheduler.DomainService.Utils
{
    public static class DateTimeExtensions
    {
        public static bool IsInRange(this DateTime dateToCheck, DateTime startDate, DateTime endDate) => dateToCheck >= startDate && dateToCheck < endDate;

        public static bool IsValid(this DateTime? dateToCheck) => dateToCheck.HasValue && dateToCheck.Value != DateTime.MinValue && dateToCheck.Value != DateTime.MaxValue;
    }
}
