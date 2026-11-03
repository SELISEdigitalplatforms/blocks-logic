import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTransformMergeV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRANSFORM_MERGE_V2.MERGE")}
      description={t("NODE_GUIDE_TRANSFORM_MERGE_V2.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.SET_NUMBER_OF_INPUTS"),
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.CHOOSE_RUN_WHEN_ALL"),
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.CONNECT_THE_NEXT_NODE"),
      ]}
      notes={[
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.MERGE_WAITS_UNTIL_EVERY"),
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.AN_INPUT_THAT_RECEIVES"),
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.IF_NO_INPUT_RECEIVES"),
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.LOWERING_NUMBER_OF_INPUTS"),
        t("NODE_GUIDE_TRANSFORM_MERGE_V2.EACH_ITEM_KEEPS_THE"),
      ]}
    />
  );
};
