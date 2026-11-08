import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Proxy } from "../types";
import { downloadExportFile, exportFileName } from "../utils";
import { useExportProxy } from "./use-proxy-api";
import { useT } from "@seliseblocks/genesis-os";

/** Exports one proxy as a downloaded .json file, with the success/failure toasts. */
export const useProxyExport = () => {
  const { t } = useT("logic.proxy");
  const exportProxy = useExportProxy();

  const handleExport = async (proxy: Pick<Proxy, "id" | "slug">) => {
    try {
      const file = await exportProxy.mutateAsync(proxy.id);
      downloadExportFile(exportFileName(proxy.slug), file);
      showSuccessToast({
        description: file.maskedValues?.length
          ? t("USE_PROXY_EXPORT.PROXY_EXPORTED_HIDDEN_VALUES")
          : t("USE_PROXY_EXPORT.PROXY_EXPORTED"),
      });
    } catch {
      showErrorToast({ errors: t("USE_PROXY_EXPORT.FAILED_TO_EXPORT_PROXY") });
    }
  };

  return { handleExport, isPending: exportProxy.isPending };
};
