import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionDataActionV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_DATA_ACTION_V1.DATA_ACTION_ACTION")}
      description={t("NODE_GUIDE_ACTION_DATA_ACTION_V1.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.KEEP_RAW_QUERY_MODE"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.IF_AUTHENTICATION_IS_NEEDED"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.FOR_GUIDED_MODE_TURN"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.FOR_GET_UPDATE_OR"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.FOR_GET_REVIEW_THE"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.RAW_QUERY_RUNS_ONCE"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.GUIDED_GET_RETURNS_ONE"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.GUIDED_INSERT_UPDATE_AND"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.GUIDED_FIELD_MAPPINGS_ARE"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.RAW_QUERY_IS_RECOMMENDED"),
        t("NODE_GUIDE_ACTION_DATA_ACTION_V1.SELECTING_A_COLLECTION_CAN"),
      ]}
    />
  );
};
