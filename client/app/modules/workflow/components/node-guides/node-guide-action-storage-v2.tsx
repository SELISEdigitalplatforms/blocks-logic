import { GuideContent } from "./node-guide-content";
import { useT } from "@seliseblocks/genesis-os";

export const NodeGuideActionStorageV2 = () => {
  const { t } = useT("logic.workflow.nodes.guides");
  return (
    <GuideContent
      title={t("NODE_GUIDE_ACTION_STORAGE_V2.STORAGE_ACTION")}
      description={t("NODE_GUIDE_ACTION_STORAGE_V2.USE_THIS_NODE_TO")}
      steps={[
        t("NODE_GUIDE_ACTION_STORAGE_V2.CHOOSE_AN_OPERATION"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.CHOOSE_AUTHENTICATION"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.ENTER_THE_FILE_ID"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.FOR_RENAME_OR_DELETE"),
      ]}
      notes={[
        t("NODE_GUIDE_ACTION_STORAGE_V2.GET_OUTPUT"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.THE_URL_EXPIRES"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.DELETE_AND_RENAME_OUTPUT"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.WEBHOOK_FILES_ACCESS"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.ONE_FILE_PER_ITEM"),
        t("NODE_GUIDE_ACTION_STORAGE_V2.ERRORS_PER_ITEM"),
      ]}
    />
  );
};
