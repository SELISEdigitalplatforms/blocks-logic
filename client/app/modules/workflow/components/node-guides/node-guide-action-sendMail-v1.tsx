import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionSendMailV1 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_SEND_MAIL_V1.SEND_MAIL_ACTION")}
      description={t("NODE_GUIDE_ACTION_SEND_MAIL_V1.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.SELECT_THE_TEMPLATE_YOU"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.CHOOSE_THE_LANGUAGE_VERSION"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.ENTER_THE_RECIPIENT_IN"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.USE_MAP_BODY_TO"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.OPTIONALLY_ADD_ONE_OR"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.TEST_WITH_A_SAFE"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.THE_NODE_SENDS_ONE"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.THE_OUTPUT_INCLUDES_SUCCESS"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.THE_OUTPUT_ALSO_INCLUDES"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.ATTACHMENTS_LEFT_BLANK_OR"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.TEMPLATE_AND_LANGUAGE_OPTIONS"),
        t("NODE_GUIDE_ACTION_SEND_MAIL_V1.THE_BODY_MAPPING_KEYS"),
      ]}
    />
  );
};
