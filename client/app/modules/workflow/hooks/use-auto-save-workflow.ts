import { useEffect, useRef, useCallback } from "react";
import { useWorkflowStore } from "../store/workflow-store";
import { WorkflowNode } from "@blocks-workflow/models/node.model";
import { useUpdateWorkflow } from "./use-workflow-api";

type UseAutoSaveWorkflowOptions = {
  workflowId: string;
  projectKey: string;
  debounceMs?: number;
  enabled?: boolean;
  onSaveSuccess?: () => void;
  onSaveError?: (error: Error) => void;
};

export const useAutoSaveWorkflow = ({
  workflowId,
  projectKey,
  debounceMs = 10000,
  enabled = true,
  onSaveSuccess,
  onSaveError,
}: UseAutoSaveWorkflowOptions) => {
  const isDirty = useWorkflowStore((state) => state.isDirty);
  const { mutateAsync, isPending } = useUpdateWorkflow();
  const debounceTimerRef = useRef<NodeJS.Timeout | null>(null);
  const isSavingRef = useRef(false);

  const saveWorkflow = useCallback(async () => {
    if (!isDirty || isSavingRef.current) return;

    isSavingRef.current = true;

    try {
      const nodesMap = useWorkflowStore.getState().nodesMap;
      const edgesMap = useWorkflowStore.getState().edgesMap;

      const nodes = Object.values(nodesMap) as WorkflowNode[];
      const edges = Object.values(edgesMap);

      await mutateAsync({
        itemId: workflowId,
        projectKey,
        nodes,
        edges,
      });

      onSaveSuccess?.();
    } catch (error) {
      onSaveError?.(error as Error);
    } finally {
      isSavingRef.current = false;
    }
  }, [isDirty, workflowId, projectKey, mutateAsync, onSaveSuccess, onSaveError]);

  useEffect(() => {
    if (!enabled || !isDirty) return;

    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    debounceTimerRef.current = setTimeout(() => {
      saveWorkflow();
    }, debounceMs);

    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, [isDirty, enabled, debounceMs, saveWorkflow]);

  useEffect(() => {
    return () => {
      if (isDirty && enabled && debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
        saveWorkflow();
      }
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return {
    isSaving: isPending || isSavingRef.current,
    saveNow: saveWorkflow,
  };
};
