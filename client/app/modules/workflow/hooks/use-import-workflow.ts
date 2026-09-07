import { useCallback, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useCreateWorkflow, useUpdateWorkflow } from "./use-workflow-api";
import {
  IMPORT_ERROR_MESSAGES,
  importSuccessMessage,
  preflightWorkflowFile,
  remapAndSanitiseWorkflow,
} from "../utils/workflow-import";

/**
 * Create-then-Update orchestration for importing a workflow file, mirroring how
 * every workflow in the app is built (create blank → save nodes/edges) and how
 * DuplicateWorkflow navigates into the new workflow.
 */
export const useImportWorkflow = () => {
  const [isImporting, setIsImporting] = useState(false);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { mutateAsync: createWorkflow } = useCreateWorkflow();
  const { mutateAsync: updateWorkflow } = useUpdateWorkflow();

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
        const { root } = preflight;

        let newWorkflowId: string | undefined;
        try {
          const created = await createWorkflow({
            name: root.name,
            description: root.description ?? "",
          });
          if (created?.isSuccess && created.itemId) {
            newWorkflowId = created.itemId;
          }
        } catch {
          newWorkflowId = undefined;
        }
        if (!newWorkflowId) {
          showErrorToast({ errors: IMPORT_ERROR_MESSAGES.CREATE_FAILED });
          return;
        }

        const { nodes, edges, settings, issues } = remapAndSanitiseWorkflow(root);

        let updateOk = false;
        try {
          const updated = await updateWorkflow({
            itemId: newWorkflowId,
            nodes: nodes as never,
            edges: edges as never,
            settings: settings as Record<string, unknown>,
          });
          updateOk = Boolean(updated?.isSuccess);
        } catch {
          updateOk = false;
        }

        queryClient.invalidateQueries({ queryKey: ["workflows"] });

        if (!updateOk) {
          showErrorToast({ errors: IMPORT_ERROR_MESSAGES.UPDATE_FAILED });
        } else {
          showSuccessToast({ description: importSuccessMessage(issues) });
        }
        navigate(scoped(`workflow/${newWorkflowId}`));
      } finally {
        setIsImporting(false);
      }
    },
    [
      isImporting,
      createWorkflow,
      updateWorkflow,
      queryClient,
      navigate,
      scoped,
    ],
  );

  return { importWorkflow, isImporting };
};
