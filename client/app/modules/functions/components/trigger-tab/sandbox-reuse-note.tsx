import { Info } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { renderRichText } from "@/lib/rich-text";

/**
 * Why repeat calls are faster. Information, not a setting: a deployed function stays loaded
 * between web calls (2026-10-06), and these are the rules that keep it correct. Tests and
 * workflow steps start fresh.
 */
export const SandboxReuseNote = () => {
  const { t } = useT("logic.functions.components");

  return (
    <Collapsible>
      <CollapsibleTrigger asChild>
        <button
          type="button"
          className="inline-flex items-center gap-1.5 text-xs font-medium text-medium-emphasis hover:text-foreground"
        >
          <Info className="h-4 w-4" aria-hidden="true" />
          <span>{t("TRIGGER_TAB.WHY_REPEAT_CALLS_ARE_FASTER")}</span>
        </button>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div
          className="mt-2 flex flex-col gap-1 rounded-lg border bg-surface-app p-3 text-xs leading-relaxed text-medium-emphasis"
          data-testid="reuse-rules"
        >
          <span>{t("TRIGGER_TAB.REUSE_INTRO")}</span>
          <span>{t("TRIGGER_TAB.REUSE_OWN_INPUT")}</span>
          <span>{t("TRIGGER_TAB.REUSE_NO_SHARED_DATA")}</span>
          <span>
            {renderRichText(t("TRIGGER_TAB.REUSE_FINISH_WORK"), [
              (text) => <code className="font-mono">{text}</code>,
            ])}
          </span>
          <span>{t("TRIGGER_TAB.REUSE_STILL_RUNNING")}</span>
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
};
