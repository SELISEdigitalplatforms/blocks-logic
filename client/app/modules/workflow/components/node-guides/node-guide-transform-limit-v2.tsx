import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTransformLimitV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRANSFORM_LIMIT_V2.LIMIT")}
      description={t("NODE_GUIDE_TRANSFORM_LIMIT_V2.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_TRANSFORM_LIMIT_V2.ENTER_MAX_ITEMS_THE"),
        t("NODE_GUIDE_TRANSFORM_LIMIT_V2.CHOOSE_IN_KEEP_WHETHER"),
      ]}
      notes={[
        t("NODE_GUIDE_TRANSFORM_LIMIT_V2.LIMIT_RUNS_ONCE_OVER"),
        t("NODE_GUIDE_TRANSFORM_LIMIT_V2.IF_THERE_ARE_NO"),
        t("NODE_GUIDE_TRANSFORM_LIMIT_V2.KEPT_ITEMS_KEEP_THEIR"),
        t("NODE_GUIDE_TRANSFORM_LIMIT_V2.EACH_KEPT_ITEM_KEEPS"),
      ]}
    />
  );
};
