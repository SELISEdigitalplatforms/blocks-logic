import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

/** The Action trigger's guide (node type `blocksEvent`, kept so saved workflows still load). */
export const NodeGuideTriggerBlocksEventV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_ACTION_V2.ACTION_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_ACTION_V2.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_ACTION_V2.SELECT_THE_ACTION_TO"),
        t("NODE_GUIDE_TRIGGER_ACTION_V2.TO_TURN_ON_MORE"),
        t("NODE_GUIDE_TRIGGER_ACTION_V2.REVIEW_THE_OUTPUT_PREVIEW"),
        t("NODE_GUIDE_TRIGGER_ACTION_V2.PUBLISH_THE_WORKFLOW_ONLY"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_ACTION_V2.EACH_ACTION_STARTS_EVERY"),
        t("NODE_GUIDE_TRIGGER_ACTION_V2.THE_OUTPUT_HOLDS"),
        t("NODE_GUIDE_TRIGGER_ACTION_V2.PUBLISHING_FAILS_FOR_UNKNOWN"),
        t("NODE_GUIDE_TRIGGER_ACTION_V2.THERE_IS_NO_EDITOR"),
      ]}
    />
  );
};
