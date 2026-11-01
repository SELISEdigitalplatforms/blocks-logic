import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTriggerBlocksEventV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.BLOCKS_EVENT_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.SELECT_THE_EVENT_TO"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.TO_ENABLE_MORE_EVENTS"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.REVIEW_THE_OUTPUT_PREVIEW"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.PUBLISH_THE_WORKFLOW_ONLY"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.EACH_EVENT_STARTS_EVERY"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.THE_OUTPUT_HOLDS_EVENTID"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.A_PAYLOAD_THAT_IS"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.PUBLISHING_FAILS_WHILE_THE"),
        t("NODE_GUIDE_TRIGGER_BLOCKS_EVENT_V2.THERE_IS_NO_EDITOR"),
      ]}
    />
  );
};
