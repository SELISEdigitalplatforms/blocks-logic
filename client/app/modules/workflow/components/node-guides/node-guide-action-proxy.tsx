import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionProxy = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_PROXY.PROXY_ACTION")}
      description={t("NODE_GUIDE_ACTION_PROXY.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_ACTION_PROXY.PICK_THE_PROXY_ONLY"),
        t("NODE_GUIDE_ACTION_PROXY.PICK_THE_ENDPOINT_THE"),
        t("NODE_GUIDE_ACTION_PROXY.FILL_IN_ANY_PATH"),
        t("NODE_GUIDE_ACTION_PROXY.TURN_ON_SEND_QUERY"),
        t("NODE_GUIDE_ACTION_PROXY.TURN_ON_SEND_BODY"),
        t("NODE_GUIDE_ACTION_PROXY.RUN_THE_WORKFLOW_AND"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_PROXY.THE_NODE_CALLS_THE"),
        t("NODE_GUIDE_ACTION_PROXY.ONLY_ENDPOINTS_ON_THE"),
        t("NODE_GUIDE_ACTION_PROXY.CALLS_ARE_RECORDED_IN"),
        t("NODE_GUIDE_ACTION_PROXY.THE_NODE_SENDS_ONE"),
        t("NODE_GUIDE_ACTION_PROXY.A_PATH_PARAMETER_FILLS"),
        t("NODE_GUIDE_ACTION_PROXY.THE_PROXY_S_OWN"),
        t("NODE_GUIDE_ACTION_PROXY.IN_THE_BODY_JSON"),
        t("NODE_GUIDE_ACTION_PROXY.A_JSON_ARRAY_RESPONSE"),
        t("NODE_GUIDE_ACTION_PROXY.IF_THE_ENDPOINT_FILTERS"),
        t("NODE_GUIDE_ACTION_PROXY.CHANGING_THE_PROXY_CLEARS"),
      ]}
    />
  );
};
