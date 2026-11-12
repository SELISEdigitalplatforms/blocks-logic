import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionIamUserV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_IAM_USER_V2.IAM_USER_ACTION")}
      description={t("NODE_GUIDE_ACTION_IAM_USER_V2.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_ACTION_IAM_USER_V2.CHOOSE_THE_AUTHENTICATION"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.CHOOSE_THE_OPERATION"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.FILL_THE_FIELDS"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.RUN_THE_NODE"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_IAM_USER_V2.A_FAILED_ITEM_BECOMES"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.CREATE_WITH_AN_EXISTING_EMAIL"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.ADD_READS_THEN_WRITES"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.SIGN_OUT"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.OUTCOME_UNKNOWN"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.ACTION_LOOP"),
        t("NODE_GUIDE_ACTION_IAM_USER_V2.THIS_ENVIRONMENTS_IAM"),
      ]}
    />
  );
};
