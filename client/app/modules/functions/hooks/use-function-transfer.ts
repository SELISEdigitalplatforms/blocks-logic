import { useMutation, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { IFunctionExportFile, IFunctionImportApplyPayload } from "../types/transfer.types";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

export const useExportFunction = () =>
  useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "export"],
    mutationFn: (functionId: string) => functionService.exportFunction(functionId),
  });

/** Writes nothing, so nothing is invalidated. */
export const usePreviewFunctionImport = () =>
  useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "import-preview"],
    mutationFn: (file: IFunctionExportFile) => functionService.previewImport(file),
  });

export const useApplyFunctionImport = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "import"],
    mutationFn: (payload: IFunctionImportApplyPayload) => functionService.applyImport(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
    },
  });
};
