import { useCallback, useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { useProjectStore, useScopedPath } from "@seliseblocks/genesis-os";
import { v4 as uuidv4 } from "uuid";
import { showErrorToast, showInfoToast, showSuccessToast } from "@/hooks/use-toast";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { ModuleName } from "@/constants/modules.constants";
import { useEnqueueWorkflowImport } from "./use-workflow-api";
import { workflowService } from "../services/workflow.service";
import {
  IMPORT_ERROR_MESSAGES,
  IMPORT_SLOW_MESSAGE,
  IMPORT_STARTED_MESSAGE,
  IMPORT_SUCCESS_MESSAGE,
  extractImportNotification,
  importSuccessMessage,
  preflightWorkflowFile,
} from "../utils/workflow-import";

/** Backup check of the list while the import's result notification has not come: every 3 s, for 60 s. */
export const IMPORT_POLL_MS = 3000;
export const IMPORT_POLL_TRIES = 20;

// The list's `search` is a server-side regex: a name like "Orders (v2)" must match itself only.
const escapeRegex = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** Ids of the workflows named exactly `name` (newest first, as the list sorts them). */
const workflowIdsNamed = async (name: string): Promise<string[]> => {
  const response = await workflowService.getWorkflows({
    search: escapeRegex(name),
    pageNumber: 0,
    pageSize: 50,
  });
  return (response?.data ?? []).filter((w) => w.name === name).map((w) => w.itemId);
};

export const useImportWorkflow = () => {
  const [isImporting, setIsImporting] = useState(false);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync: enqueueImport } = useEnqueueWorkflowImport();
  const pendingRef = useRef(new Set<string>());
  const pollTimersRef = useRef(new Map<string, ReturnType<typeof setTimeout>>());

  const stopPolling = useCallback((correlationId: string) => {
    const timer = pollTimersRef.current.get(correlationId);
    if (timer) clearTimeout(timer);
    pollTimersRef.current.delete(correlationId);
  }, []);

  // No poll may outlive the page (no toast or navigation after the user left).
  useEffect(() => {
    const timers = pollTimersRef.current;
    return () => {
      timers.forEach((timer) => clearTimeout(timer));
      timers.clear();
    };
  }, []);

  const onImportNotification = useCallback(
    (data: unknown) => {
      const notification = extractImportNotification(data);
      if (!notification || !pendingRef.current.has(notification.correlationId)) return;
      pendingRef.current.delete(notification.correlationId);
      stopPolling(notification.correlationId);

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
    [queryClient, navigate, scoped, stopPolling],
  );

  // The server sends the result on the workflow notification configuration (its event is
  // `WorkflowNotification`); `workflow-import` stays for a deployment that set that one up.
  // Execution events on the same channel carry no matching correlationId and are ignored.
  useNotificationListener("WorkflowNotification", onImportNotification);
  useNotificationListener("workflow-import", onImportNotification);

  /**
   * The backup when no notification arrives (channel not set up, socket down): the new workflow
   * is the one named like the file that was not there before. First of the two wins; the other
   * then finds the import no longer pending. A failure is only told by the notification, so the
   * end of the window says "taking longer", never "failed".
   */
  const pollForImported = useCallback(
    (correlationId: string, name: string, before: Set<string>) => {
      const schedule = (tries: number) => {
        const timer = setTimeout(async () => {
          if (!pendingRef.current.has(correlationId)) return;
          let found: string | undefined;
          try {
            found = (await workflowIdsNamed(name)).find((id) => !before.has(id));
          } catch {
            // A failed check is not a failed import: try again on the next tick.
          }
          if (!pendingRef.current.has(correlationId) || !pollTimersRef.current.has(correlationId))
            return;
          if (found) {
            pendingRef.current.delete(correlationId);
            pollTimersRef.current.delete(correlationId);
            queryClient.invalidateQueries({ queryKey: ["workflows"] });
            showSuccessToast({ description: IMPORT_SUCCESS_MESSAGE });
            navigate(scoped(`workflow/${found}`));
            return;
          }
          if (tries + 1 >= IMPORT_POLL_TRIES) {
            // Still pending: a late notification can still finish it.
            pollTimersRef.current.delete(correlationId);
            queryClient.invalidateQueries({ queryKey: ["workflows"] });
            showInfoToast({ description: IMPORT_SLOW_MESSAGE });
            return;
          }
          schedule(tries + 1);
        }, IMPORT_POLL_MS);
        pollTimersRef.current.set(correlationId, timer);
      };
      schedule(0);
    },
    [queryClient, navigate, scoped],
  );

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

        // Taken before the import exists, so the backup check can tell the new workflow from an
        // older one with the same name. Unreadable → no backup check (it could pick the wrong one).
        const name = preflight.root.name;
        let before: Set<string> | null = null;
        try {
          before = new Set(await workflowIdsNamed(name));
        } catch {
          before = null;
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
        if (before && pendingRef.current.has(correlationId))
          pollForImported(correlationId, name, before);
      } finally {
        setIsImporting(false);
      }
    },
    [isImporting, enqueueImport, projectKey, pollForImported],
  );

  return { importWorkflow, isImporting };
};
