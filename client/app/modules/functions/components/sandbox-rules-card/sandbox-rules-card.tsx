import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { cn } from "@/lib/utils";
import { SANDBOX_CODE_RULES } from "../../constants/limits.constant";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

/**
 * "Don't / Do instead" beside the editor, so the reuse rules are read while writing the code, not
 * only on the Trigger tab. Reuse is always on for HTTP calls of a deployed function (2026-10-06),
 * so the card is open by default: breaking a rule replaces the sandbox and can stop unfinished work.
 */
export const SandboxRulesCard = () => {
  const { t } = useT("logic.functions.components");
  const [isOpen, setIsOpen] = useState(true);

  return (
    <Card>
      <Collapsible open={isOpen} onOpenChange={setIsOpen}>
        <CollapsibleTrigger className="flex w-full items-center justify-between gap-3 p-4 text-left">
          <span className="text-sm font-semibold">{t("SANDBOX_RULES_CARD.WHAT_NOT_TO_DO")}</span>
          <ChevronDown
            className={cn(
              "h-4 w-4 shrink-0 text-medium-emphasis transition-transform",
              isOpen && "rotate-180",
            )}
          />
        </CollapsibleTrigger>
        <CollapsibleContent>
          <CardContent className="flex flex-col gap-3 px-4 pb-4 pt-0">
            <p
              className="rounded-md bg-surface-app p-2.5 text-xs leading-relaxed text-medium-emphasis"
              data-testid="sandbox-rules-mode"
            >
              {renderRichText(t("SANDBOX_RULES_CARD.N_0_CALLS_OF_A"), [(children) => <strong className="font-semibold">{children}</strong>])}
            </p>
            <ol className="flex flex-col gap-3" data-testid="sandbox-rules">
              {SANDBOX_CODE_RULES.map((rule) => (
                <li
                  key={rule.dont}
                  className="flex flex-col gap-1.5 border-t pt-3 first:border-t-0 first:pt-0"
                >
                  <span className="text-xs leading-relaxed">
                    <strong className="font-semibold text-error">{t("SANDBOX_RULES_CARD.DON_T")}</strong> {rule.dont}
                  </span>
                  <pre className="overflow-x-auto whitespace-pre rounded-md bg-surface-app p-2 font-mono text-[11px] leading-relaxed text-medium-emphasis">
                    {rule.dontCode}
                  </pre>
                  <span className="text-xs leading-relaxed">
                    <strong className="font-semibold text-success">{t("SANDBOX_RULES_CARD.DO")}</strong> {rule.fix}
                  </span>
                  <pre className="overflow-x-auto whitespace-pre rounded-md bg-surface-app p-2 font-mono text-[11px] leading-relaxed text-medium-emphasis">
                    {rule.fixCode}
                  </pre>
                </li>
              ))}
            </ol>
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  );
};
