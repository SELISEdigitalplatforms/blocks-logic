import { useCallback, useEffect } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { IFunctionDetail } from "../types/function.types";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";
import { useFunctionEditorStore } from "../store/function-editor-store";

/**
 * Drives the Code+Configuration editor: hydrates the store from a loaded function, saves the
 * whole working copy in one round trip (`SaveFunctionRequestDto`), and binds Ctrl+S to save.
 */
export const useFunctionEditor = (functionId: string | undefined, functionDetail?: IFunctionDetail) => {
  const queryClient = useQueryClient();
  const hydrate = useFunctionEditorStore((s) => s.hydrate);
  const isDirty = useFunctionEditorStore((s) => s.isDirty);

  useEffect(() => {
    if (!functionDetail) return;
    hydrate({
      indexJs: functionDetail.indexJs,
      packageJson: functionDetail.packageJson,
      limits: functionDetail.limits,
      retry: functionDetail.retry,
      trigger: functionDetail.trigger,
      outputActions: functionDetail.outputActions,
      variables: functionDetail.variables,
    });
    // Only re-hydrate when a different function's data arrives, not on every store write.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [functionDetail?.id]);

  const { mutateAsync: saveMutateAsync, isPending: isSaving } = useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "save"],
    mutationFn: () => {
      const state = useFunctionEditorStore.getState();
      return functionService.saveFunction({
        functionId: functionId!,
        indexJs: state.indexJs,
        packageJson: state.packageJson,
        limits: state.limits,
        retry: state.retry,
        trigger: state.trigger,
        outputActions: state.outputActions,
        variables: state.variables,
      });
    },
    onSuccess: (saved) => {
      useFunctionEditorStore.getState().hydrate({
        indexJs: saved.indexJs,
        packageJson: saved.packageJson,
        limits: saved.limits,
        retry: saved.retry,
        trigger: saved.trigger,
        outputActions: saved.outputActions,
        variables: saved.variables,
      });
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
    },
  });

  const save = useCallback(async () => {
    if (!functionId) return;
    try {
      await saveMutateAsync();
      showSuccessToast({ description: "Function saved." });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to save function." });
    }
  }, [functionId, saveMutateAsync]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const isSaveShortcut = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "s";
      if (!isSaveShortcut) return;
      event.preventDefault();
      if (!isSaving) void save();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [save, isSaving]);

  return { save, isSaving, isDirty };
};
