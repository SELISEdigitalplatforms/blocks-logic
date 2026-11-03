import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTriggerEmailV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_EMAIL_V1.EMAIL_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_EMAIL_V1.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.SELECT_THE_MAILBOX_THE"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.MAKE_SURE_THE_MAILBOX"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.USE_A_TEST_EMAIL"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.MAP_FIELDS_FROM_THE"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.THE_BACKEND_STARTS_WORKFLOWS"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.THE_TRIGGER_OUTPUT_IS"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.MAILBOX_OPTIONS_ARE_LOADED"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.SELECTING_A_MAILBOX_STORES"),
        t("NODE_GUIDE_TRIGGER_EMAIL_V1.THE_PUBLISHED_WORKFLOW_RUNS"),
      ]}
    />
  );
};
