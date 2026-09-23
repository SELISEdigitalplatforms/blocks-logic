import { useCallback, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { useProjectStore, useScopedPath } from "@seliseblocks/genesis-os";
import { v4 as uuidv4 } from "uuid";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { ModuleName } from "@/constants/modules.constants";
import { useEnqueueWorkflowImport } from "./use-workflow-api";
import { workflowService } from "../services/workflow.service";
import {
  IMPORT_ERROR_MESSAGES,
  IMPORT_STARTED_MESSAGE,
  extractImportNotification,
  importSuccessMessage,
  preflightWorkflowFile,
} from "../utils/workflow-import";

export const useImportWorkflow = () => {
  const [isImporting, setIsImporting] = useState(false);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync: enqueueImport } = useEnqueueWorkflowImport();
  const pendingRef = useRef(new Set<string>());

  const onImportNotification = useCallback(
    (data: unknown) => {
      const notification = extractImportNotification(data);
      if (!notification || !pendingRef.current.has(notification.correlationId)) return;
      pendingRef.current.delete(notification.correlationId);

      if (!notification.isSuccess) {
        showErrorToast({
          errors: notification.description || IMPORT_ERROR_MESSAGES.ENQUEUE_FAILED,
        });
        return;
      }

      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      showSuccessToast({ description: importSuccessMessage(notification.issues) });
      if (notification.workflowId) {
        navigate(scoped(`workflow/${notification.workflowId}`));
      }
    },
    [queryClient, navigate, scoped],
  );

  useNotificationListener("workflow-import", onImportNotification);

  const importWorkflow = useCallback(
    async (file: File) => {
      if (!file || isImporting) return;
      setIsImporting(true);
      try {
        const text = await file.text();
        const preflight = preflightWorkflowFile({ text, size: file.size });
        if (!preflight.ok) {
          showErrorToast({ errors: preflight.message });
          return;
        }

        const presign = await workflowService.getPreSignedUrlForUpload({
          itemId: "",
          name: file.name,
          configurationName: "Default",
          projectKey,
          metaData: "",
          parentDirectoryId: "",
          tags: "",
          accessModifier: "Private",
          moduleName: ModuleName.DefaultCloud,
        });
        if (!presign?.isSuccess || !presign.fileId || !presign.uploadUrl) {
          showErrorToast({ errors: IMPORT_ERROR_MESSAGES.UPLOAD_FAILED });
          return;
        }

        try {
          await workflowService.uploadFileToPresignedUrl(presign.uploadUrl, file);
        } catch {
          showErrorToast({ errors: IMPORT_ERROR_MESSAGES.UPLOAD_FAILED });
          return;
        }

        const correlationId = uuidv4();
        pendingRef.current.add(correlationId);
        try {
          const enqueued = await enqueueImport({
            fileId: presign.fileId,
            messageCoRelationId: correlationId,
          });
          if (!enqueued?.isSuccess) {
            pendingRef.current.delete(correlationId);
            showErrorToast({ errors: IMPORT_ERROR_MESSAGES.ENQUEUE_FAILED });
            return;
          }
        } catch {
          pendingRef.current.delete(correlationId);
          showErrorToast({ errors: IMPORT_ERROR_MESSAGES.ENQUEUE_FAILED });
          return;
        }

        showSuccessToast({ description: IMPORT_STARTED_MESSAGE });
      } finally {
        setIsImporting(false);
      }
    },
    [isImporting, enqueueImport, projectKey],
  );

  return { importWorkflow, isImporting };
};
