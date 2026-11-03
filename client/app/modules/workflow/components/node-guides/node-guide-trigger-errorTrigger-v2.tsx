import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTriggerErrorTriggerV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.ERROR_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.ADD_THE_ERROR_TRIGGER"),
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.PUBLISH_THIS_WORKFLOW"),
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.OPEN_THE_OTHER_WORKFLOW"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.THE_OUTPUT_HAS_THE"),
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.TURN_ON_RUN_ON"),
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.TEST_STEP_ON_THIS"),
        t("NODE_GUIDE_TRIGGER_ERROR_TRIGGER_V2.A_WORKFLOW_CAN_HAVE"),
      ]}
    />
  );
};
