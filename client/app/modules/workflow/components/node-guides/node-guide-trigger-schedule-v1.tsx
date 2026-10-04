import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerScheduleV1 = () => (
  <GuideContent
    title="Schedule trigger"
    description="Use this trigger to start a workflow on a time schedule. The guided interval fields generate a 5-field cron expression, and Custom lets you enter the cron expression yourself."
    steps={[
      "Choose a Trigger Interval: minutes, hours, days, weeks, or custom cron.",
      "Fill the timing fields that appear for the selected interval, such as minute, hour, weekday, or day of month.",
      "Use Cron Preview to confirm the generated expression.",
      "Choose Custom (Cron) only when you want to type the 5-field cron expression directly.",
    ]}
    notes={[
      "The saved parameters are normalized to triggerInterval and cronExpression; the helper timing fields are transient.",
      "Published schedule runs receive output fields such as WorkflowId, TriggerId, TenantId, CronExpression, and FiredAt.",
      "Monthly schedules limit the day of month to 1-28 so the day exists in every month.",
      "Hourly schedules use cron stepping, so intervals that do not divide 24 evenly run on matching clock hours rather than exact elapsed-hour spacing.",
    ]}
  />
);
