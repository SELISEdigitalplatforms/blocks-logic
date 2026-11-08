import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTriggerDataGatewayV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.DATA_GATEWAY_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.SELECT_THE_COLLECTION_TO"),
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.CHOOSE_THE_OPERATION_THAT"),
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.REVIEW_THE_OUTPUT_PREVIEW"),
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.IN_EDITOR_TEST_MODE"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.THE_X_BLOCKS_KEY"),
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.INSERT_AND_DELETE_PAYLOADS"),
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.UPDATE_PAYLOADS_INCLUDE_UPDATEDFIELDS"),
        t("NODE_GUIDE_TRIGGER_DATA_GATEWAY_V1.A_CHANGE_RUNS_MATCHING"),
      ]}
    />
  );
};
