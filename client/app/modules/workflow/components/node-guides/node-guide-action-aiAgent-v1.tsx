import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionAiAgentV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_AI_AGENT_V1.AI_AGENT_ACTION")}
      description={t("NODE_GUIDE_ACTION_AI_AGENT_V1.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.CHOOSE_THE_AGENT_THAT"),
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.WRITE_THE_INPUT_MESSAGE"),
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.TEST_THE_NODE_WITH"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.THE_NODE_CALLS_THE"),
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.THE_OUTPUT_IS_THE"),
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.THE_AGENT_LIST_IS"),
        t("NODE_GUIDE_ACTION_AI_AGENT_V1.THE_NODE_STORES_AGENT"),
      ]}
    />
  );
};
