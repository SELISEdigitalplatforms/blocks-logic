using DataAccess.InternalService.Notifications;

namespace Scheduler.DomainService.Services
{
    /// <summary>
    /// Tells the user who owns a schedule that it could not be registered, so it will not run
    /// (built-in <c>logic-schedule-registration-failed</c>). Best effort: never throws.
    /// </summary>
    internal static class ScheduleRegistrationNotifier
    {
        internal static async Task NotifyAsync(
            IBuiltInNotificationSender? sender, string? tenantId, string? owner, string scheduleId, string? workflowId)
        {
            if (sender is null || string.IsNullOrWhiteSpace(tenantId)) return;

            try
            {
                await sender.SendAsync(
                    tenantId,
                    BuiltInNotificationNames.ScheduleRegistrationFailed,
                    string.IsNullOrWhiteSpace(owner) ? null : [owner],
                    new BuiltInNotificationPayload(
                        "ScheduleRegistrationFailed",
                        "Schedule not registered",
                        "A schedule could not be registered, so it will not run.",
                        IsSuccess: false,
                        Ids: new Dictionary<string, string?> { ["scheduleId"] = scheduleId, ["workflowId"] = workflowId }),
                    throttleKey: scheduleId);
            }
            catch (Exception)
            {
                // Best effort by contract.
            }
        }
    }
}
