import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Proxy } from "../types";
import { downloadExportFile, exportFileName } from "../utils";
import { useExportProxy } from "./use-proxy-api";

/** Exports one proxy as a downloaded .json file, with the success/failure toasts. */
export const useProxyExport = () => {
  const exportProxy = useExportProxy();

  const handleExport = async (proxy: Pick<Proxy, "id" | "slug">) => {
    try {
      const file = await exportProxy.mutateAsync(proxy.id);
      downloadExportFile(exportFileName(proxy.slug), file);
      showSuccessToast({
        description: file.maskedValues?.length
          ? "Proxy exported. Hidden values are saved as ***."
          : "Proxy exported.",
      });
    } catch {
      showErrorToast({ errors: "Failed to export proxy" });
    }
  };

  return { handleExport, isPending: exportProxy.isPending };
};
