import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideLogicSwitchV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_LOGIC_SWITCH_V2.SWITCH")}
      description={t("NODE_GUIDE_LOGIC_SWITCH_V2.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_LOGIC_SWITCH_V2.ADD_ONE_ROUTING_RULE"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.IN_EACH_RULE_PICK"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.TURN_ON_RENAME_OUTPUT"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.CHOOSE_WHAT_HAPPENS_TO"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.CONNECT_THE_FOLLOWING_NODES"),
      ]}
      notes={[
        t("NODE_GUIDE_LOGIC_SWITCH_V2.CONDITIONS_WORK_EXACTLY_LIKE"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.A_VALUE_THAT_IS"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.RENAMING_OR_DELETING_AN"),
        t("NODE_GUIDE_LOGIC_SWITCH_V2.AN_ITEM_WHOSE_CONDITIONS"),
      ]}
    />
  );
};
