import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideLogicIfV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_LOGIC_IF_V1.IF_LOGIC")}
      description={t("NODE_GUIDE_LOGIC_IF_V1.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_LOGIC_IF_V1.USE_ALL_CONDITIONS_AND"),
        t("NODE_GUIDE_LOGIC_IF_V1.USE_ANY_CONDITION_OR"),
        t("NODE_GUIDE_LOGIC_IF_V1.ADD_THE_CONDITIONS_THAT"),
        t("NODE_GUIDE_LOGIC_IF_V1.CONNECT_THE_FOLLOWING_NODES"),
      ]}
      notes={[
        t("NODE_GUIDE_LOGIC_IF_V1.CONDITION_VALUES_ARE_RESOLVED"),
        t("NODE_GUIDE_LOGIC_IF_V1.NUMBER_BOOLEAN_DATE_TIME"),
        t("NODE_GUIDE_LOGIC_IF_V1.THE_NODE_STARTS_WITH"),
        t("NODE_GUIDE_LOGIC_IF_V1.EQUALS_ONE_OF_LIST"),
        t("NODE_GUIDE_LOGIC_IF_V1.AN_ITEM_WHOSE_CONDITION"),
        t("NODE_GUIDE_LOGIC_IF_V1.EMPTY_OR_INCOMPLETE_CONDITIONS"),
      ]}
    />
  );
};
