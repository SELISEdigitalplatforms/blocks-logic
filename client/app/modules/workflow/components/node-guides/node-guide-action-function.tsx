import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionFunction = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_FUNCTION.FUNCTION_ACTION")}
      description={t("NODE_GUIDE_ACTION_FUNCTION.INVOKES_A_DEPLOYED_FUNCTION")}
      steps={[
        t("NODE_GUIDE_ACTION_FUNCTION.PICK_A_FUNCTION_ONLY"),
        t("NODE_GUIDE_ACTION_FUNCTION.CHOOSE_THE_INPUT_PREVIOUS"),
        t("NODE_GUIDE_ACTION_FUNCTION.TEST_THE_WORKFLOW_AND"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_FUNCTION.ONE_INVOCATION_PER_INPUT"),
        t("NODE_GUIDE_ACTION_FUNCTION.IN_CUSTOM_JSON_MODE"),
        t("NODE_GUIDE_ACTION_FUNCTION.THE_STEP_WAITS_FOR"),
        t("NODE_GUIDE_ACTION_FUNCTION.THE_FUNCTION_RUNS_WITH"),
        t("NODE_GUIDE_ACTION_FUNCTION.A_FUNCTION_STEP_WITH"),
        t("NODE_GUIDE_ACTION_FUNCTION.THE_FUNCTION_S_RUN"),
      ]}
    />
  );
};
