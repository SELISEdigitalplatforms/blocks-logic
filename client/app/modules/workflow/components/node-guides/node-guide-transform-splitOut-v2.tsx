import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTransformSplitOutV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.SPLIT_OUT")}
      description={t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.IN_FIELDS_TO_SPLIT"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.OPTIONALLY_ENTER_AN_OUTPUT"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.CHOOSE_IN_INCLUDE_WHICH"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.ADD_ANOTHER_FIELD_TO"),
      ]}
      notes={[
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.SEVERAL_FIELDS_ARE_COMBINED"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.AN_OBJECT_IS_SPLIT"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.WITH_NO_OTHER_FIELDS"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.AN_ITEM_WITHOUT_THE"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.TURN_ON_DISABLE_DOT"),
        t("NODE_GUIDE_TRANSFORM_SPLIT_OUT_V2.EACH_NEW_ITEM_KEEPS"),
      ]}
    />
  );
};
