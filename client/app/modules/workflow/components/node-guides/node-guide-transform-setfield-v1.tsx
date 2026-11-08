import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTransformSetFieldV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.SET_FIELD_TRANSFORM")}
      description={t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.CHOOSE_MANUAL_MAPPING_TO"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.IN_MANUAL_MAPPING_CHOOSE"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.IN_JSON_MODE_ENTER"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.TURN_ON_INCLUDE_OTHER"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.WHEN_INCLUDING_INPUT_FIELDS"),
      ]}
      notes={[
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.THE_NODE_RUNS_ONCE"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.IN_JSON_MODE_VALUES"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.FIELDS_YOU_SET_ARE"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.BY_DEFAULT_OTHER_INPUT"),
        t("NODE_GUIDE_TRANSFORM_SETFIELD_V1.SPECIFIC_INCLUDE_OR_EXCLUDE"),
      ]}
    />
  );
};
