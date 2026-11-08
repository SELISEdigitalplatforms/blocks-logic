import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTriggerScheduleV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.SCHEDULE_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.CHOOSE_A_TRIGGER_INTERVAL"),
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.FILL_THE_TIMING_FIELDS"),
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.USE_CRON_PREVIEW_TO"),
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.CHOOSE_CUSTOM_CRON_ONLY"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.THE_SAVED_PARAMETERS_ARE"),
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.PUBLISHED_SCHEDULE_RUNS_RECEIVE"),
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.SCHEDULES_RUN_ON_UTC"),
        t("NODE_GUIDE_TRIGGER_SCHEDULE_V1.HOURLY_SCHEDULES_USE_CRON"),
      ]}
    />
  );
};
