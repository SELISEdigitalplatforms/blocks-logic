import { TriangleAlert } from "lucide-react";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

type NeedsSecretBannerProps = {
  missing?: string[];
  duplicate?: string[];
};

/**
 * "Needs secret": the function uses secrets by name (left by an import) that have no single match
 * here. Computed by the server on every read, so it goes away by itself once the secret exists —
 * there is nothing to edit, save or redeploy. Runs are not blocked; one that needs the secret fails
 * with a clear message until it exists.
 */
export const NeedsSecretBanner = ({ missing = [], duplicate = [] }: NeedsSecretBannerProps) => {
  const { t } = useT("logic.functions.components");
  if (missing.length === 0 && duplicate.length === 0) return null;
  return (
    <div
      role="status"
      className="flex items-start gap-2 rounded-lg border border-warning-200 bg-warning-50 px-3 py-2 text-sm text-warning-800"
    >
      <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
      <div className="flex flex-col gap-0.5">
        {missing.length > 0 && (
          <span>
            {renderRichText(t("NEEDS_SECRET_BANNER.NEEDS_SECRET_0_0", { value: missing.join(", ") }), [(children) => <strong className="font-mono">{children}</strong>])}
          </span>
        )}
        {duplicate.length > 0 && (
          <span>
            {renderRichText(t("NEEDS_SECRET_BANNER.MORE_THAN_ONE_SECRET", { value: duplicate.join(", ") }), [(children) => <strong className="font-mono">{children}</strong>])}
          </span>
        )}
      </div>
    </div>
  );
};
