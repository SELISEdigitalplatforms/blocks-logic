import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideLogicFilterV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_LOGIC_FILTER_V2.FILTER")}
      description={t("NODE_GUIDE_LOGIC_FILTER_V2.USE_THIS_NODE_TO_KEEP")}
      steps={[
        t("NODE_GUIDE_LOGIC_FILTER_V2.ADD_CONDITIONS"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.CHOOSE_AND_OR"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.CONNECT_THE_NEXT_NODE"),
      ]}
      notes={[
        t("NODE_GUIDE_LOGIC_FILTER_V2.CONDITIONS_WORK_EXACTLY_LIKE"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.TEXT_COMPARISONS_ARE_CASE_SENSITIVE"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.EVERY_CONDITION_IS_EVALUATED"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.A_VALUE_THAT_IS_NOT_VALID"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.WITH_NO_CONDITIONS"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.AN_ITEM_WHOSE_CONDITIONS_CANNOT"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.THE_EXECUTION_LOG_SHOWS"),
        t("NODE_GUIDE_LOGIC_FILTER_V2.EXAMPLE"),
      ]}
    />
  );
};
