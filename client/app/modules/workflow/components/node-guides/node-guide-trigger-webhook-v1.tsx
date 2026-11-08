import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideTriggerWebhookV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.WEBHOOK_TRIGGER")}
      description={t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.USE_THIS_TRIGGER_TO")}
      steps={[
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.USE_THE_WEBHOOK_URL"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.SEND_A_POST_REQUEST"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.THE_DEPRECATED_WEBHOOK_URL"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.CHOOSE_THE_AUTHENTICATION_LEVEL"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.FOR_BLOCKS_AUTHORIZATION_CHOOSE"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.CHOOSE_WHETHER_THE_WEBHOOK"),
      ]}
      notes={[
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.THE_TEST_URL_ONLY"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.IMMEDIATE_RESPONSE_QUEUES_THE"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.WHEN_WAITING_FOR_THE"),
        t("NODE_GUIDE_TRIGGER_WEBHOOK_V1.ROLES_AND_PERMISSIONS_CAN"),
      ]}
    />
  );
};
