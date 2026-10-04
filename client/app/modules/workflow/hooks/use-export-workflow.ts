import { useCallback, useState } from "react";
import { showErrorToast } from "@/hooks/use-toast";
import { workflowService } from "../services/workflow.service";
import { downloadJson } from "../utils/download-json.util";
import { buildWorkflowExport, workflowExportFileName } from "../utils/workflow-export";
import { IMPORT_ERROR_MESSAGES } from "../utils/workflow-import";

/**
 * Thin orchestration for the row `⋮` → Export action: fetch the working
 * workflow entity, strip it to the export shape and trigger a JSON download.
 */
export const useExportWorkflow = () => {
  const [isExporting, setIsExporting] = useState(false);

  const exportWorkflow = useCallback(async (workflowId: string) => {
    if (!workflowId) return;
    setIsExporting(true);
    try {
      const res = await workflowService.getWorkflowById({ id: workflowId });
      if (!res?.isSuccess || !res.data) {
        showErrorToast({ errors: IMPORT_ERROR_MESSAGES.EXPORT_FAILED });
        return;
      }
      const file = buildWorkflowExport(res.data);
      downloadJson(workflowExportFileName(file.name), file);
    } catch {
      showErrorToast({ errors: IMPORT_ERROR_MESSAGES.EXPORT_FAILED });
    } finally {
      setIsExporting(false);
    }
  }, []);

  return { exportWorkflow, isExporting };
};
