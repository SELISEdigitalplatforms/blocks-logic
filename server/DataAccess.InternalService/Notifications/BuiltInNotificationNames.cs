namespace DataAccess.InternalService.Notifications;

/// <summary>Names of the built-in notification configurations this service sends (seeded in every tenant).</summary>
public static class BuiltInNotificationNames
{
    public const string WorkflowRunFailed = "logic-workflow-run-failed";
    public const string ScheduledTriggerDropped = "logic-scheduled-trigger-dropped";
    public const string FunctionBuildFinished = "logic-function-build-finished";
    public const string FunctionRunFailed = "logic-function-run-failed";
    public const string MailSendFailed = "logic-mail-send-failed";
    public const string MailboxSyncFailing = "logic-mailbox-sync-failing";
    public const string ScheduleRegistrationFailed = "logic-schedule-registration-failed";
    public const string FunctionChangedByOther = "logic-function-changed-by-other";
}
